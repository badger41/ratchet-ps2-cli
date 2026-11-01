using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Decodes scene faces, alpha and native identities without generating visibility data.</summary>
internal static class UyaOcclusionSceneExtractor
{
    internal static UyaOcclusionExtractedScene Extract(ReadOnlySpan<byte> wad, CancellationToken cancellationToken, bool coverageOnly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wad.Length > UyaOcclusionLevelAnalyzer.MaximumLevelWadBytes) throw new InvalidDataException("Coverage scene exceeds the level byte limit.");
        var audit = coverageOnly ? null : UyaOcclusionLevelAnalyzer.Analyze(wad, cancellationToken);
        var sources = audit?.Sources;
        if (audit?.ChunkTerrainSources.Count > 0)
            throw new NotSupportedException("Rebuilding streamed chunk terrain is not supported yet.");
        if (audit?.Sources is { } links && (links.UnresolvedMappings.Count != 0 || links.UnmappedSources.Count != 0 || links.AmbiguousSources.Count != 0))
            throw new InvalidDataException($"Occlusion scene has {links.UnresolvedMappings.Count} unused mapping rows, {links.UnmappedSources.Count} unmapped sources and {links.AmbiguousSources.Count} ambiguous sources. Rebuilding requires existing native groups; custom group assignment is not supported yet. First unmapped: {string.Join(", ", links.UnmappedSources.Take(3))}");
        if (!coverageOnly && (audit!.Grid is null || sources is null || sources.Links.Any(link => link.BitIndex is < 0 or >= 1024)))
            throw new InvalidDataException("Occlusion generation requires primary terrain with fully resolved, valid native groups.");
        var files = UyaLevelWadUnpacker.Unpack(wad).Files.ToDictionary(file => file.Path, file => file.Bytes);
        if (coverageOnly) for (var i = 1; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.TryGetValue($"level_wad/chunks/chunk{i}.wad", out var chunk)
                && TfragChunkWadReader.ReadTerrainPayload(chunk).AsSpan().ContainsAnyExcept((byte)0))
                throw new NotSupportedException("Coverage preview of streamed chunk terrain is not supported yet.");
        }
        if (coverageOnly) foreach (var name in new[] { "tie", "shrub" })
        {
            if (files.TryGetValue($"gameplay/core/{name}_instances.bin", out var records)
                && (records.Length < 4 || System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(records) is < 0 or > 100_000))
                throw new InvalidDataException("Coverage scene exceeds its static-instance limit.");
        }
        var headerBytes = files["assets/asset_header.bin"];
        var h = LevelAssetReader.ReadHeader(headerBytes);
        var sourceAssets = files["assets/asset_wad.bin"];
        var assets = BinaryMagic.IsWad(sourceAssets) ? WadCompression.Decompress(sourceAssets, new(), cancellationToken) : sourceAssets;
        var mobyDefs = LevelAssetReader.ReadModelDefinitions(headerBytes, h.MobyModelOffset, h.MobyModelCount);
        var tieDefs = LevelAssetReader.ReadModelDefinitions(headerBytes, h.TieModelOffset, h.TieModelCount);
        var shrubDefs = LevelAssetReader.ReadShrubDefinitions(headerBytes, h.ShrubModelOffset, h.ShrubModelCount);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(h, assets.Length, mobyDefs, tieDefs, shrubDefs, [h.SceneViewSize]);
        var terrain = LevelAssetReader.ReadAssetSlice(assets, h.TerrainOffset, offsets, allowZeroOffset: true);
        var extraction = TfragPieceExtractor.Extract(coverageOnly && !terrain.AsSpan().ContainsAnyExcept((byte)0) ? [] : [new("primary", TfragPieceSourceKind.Primary, 0, terrain)], cancellationToken);
        var pieces = extraction.Assets.ToDictionary(asset => asset.ContentId, asset => asset.Geometry);
        var textureSource = new UyaOcclusionTextureAtlas(headerBytes, files["assets/palette.bin"], assets);
        var bySource = sources?.Links.ToDictionary(link => link.SourceKey, StringComparer.Ordinal) ?? [];
        int Group(string key) => coverageOnly ? -1 : bySource[key].BitIndex;
        var sourceRecords = new List<UyaOcclusionSource>();
        int Source(string key, string family, int? classId = null)
        {
            bySource.TryGetValue(key, out var link);
            sourceRecords.Add(new(key, family, link?.BitIndex ?? -1, link?.MappingIndex, link?.SecondWord, classId, null));
            return sourceRecords.Count - 1;
        }
        var currentSource = -1; var sourceFace = 0;
        var triangles = new List<OcclusionSurfaceTriangle>();
        var geometrySources = new HashSet<string>(StringComparer.Ordinal);
        var forcedSources = new HashSet<string>(StringComparer.Ordinal);
        void Force(string key)
        {
            forcedSources.Add(key);
            var index = sourceRecords.FindIndex(source => source.Key == key);
            if (index < 0) throw new InvalidDataException("Occlusion fallback source has no native identity.");
            sourceRecords[index] = sourceRecords[index] with { Fallback = "moby-motion-or-empty-mesh" };
        }
        void Add(Vector3 a, Vector3 b, Vector3 c, int group, Vector2 uvA, Vector2 uvB, Vector2 uvC, int texture, bool clampU, bool clampV)
        {
            if (triangles.Count >= UyaOcclusionLimits.MaximumTriangles)
                throw new InvalidDataException("Occlusion scene exceeds the bounded triangle limit.");
            triangles.Add(new(a, b, c, group, 3)
            {
                SourceIndex = currentSource,
                SourceFaceIndex = sourceFace++,
                UvA = uvA,
                UvB = uvB,
                UvC = uvC,
                TextureIndex = texture,
                WrapFlags = (clampU ? 1u : 0u) | (clampV ? 2u : 0u)
            });
        }
        foreach (var occurrence in extraction.Occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"tfrag:primary:group:{occurrence.Provenance.GroupIndex}";
            currentSource = Source(key, "tfrag"); sourceFace = 0;
            var group = Group(key);
            var start = triangles.Count;
            var p = occurrence.Transform.Position;
            if (occurrence.Transform.Rotation != new TfragPieceVector4(0, 0, 0, 1)
                || occurrence.Transform.Scale != new TfragPieceVector3(1, 1, 1))
                throw new InvalidDataException("Unexpected source tfrag transform.");
            var lods = pieces[occurrence.ContentId].Lods;
            var lodIndex = UyaOcclusionMeshLods.SelectLod0(lods.Select(lod => (lod.LodIndex, lod.Primitives.Sum(p => p.Indices.Count / 3))));
            var selected = lods.Single(lod => lod.LodIndex == lodIndex);
            foreach (var primitive in selected.Primitives)
            {
                var texture = textureSource.Resolve("tfrag", primitive.Material.TextureId);
                Vector3 Position(uint i)
                {
                    var v = primitive.Vertices[checked((int)i)].Position;
                    return new(v.X + p.X, v.Y + p.Y, v.Z + p.Z);
                }
                for (var i = 0; i < primitive.Indices.Count; i += 3)
                {
                    var a = primitive.Indices[i]; var b = primitive.Indices[i + 1]; var c = primitive.Indices[i + 2];
                    Vector2 Uv(uint index) { var uv = primitive.Vertices[(int)index].TexCoord; return new(uv.X, uv.Y); }
                    Add(Position(a), Position(b), Position(c), group, Uv(a), Uv(b), Uv(c), texture, primitive.Material.ClampU, primitive.Material.ClampV);
                }
            }
            if (triangles.Count == start) Force(key); else geometrySources.Add(key);
        }
        var ties = files.TryGetValue("gameplay/core/tie_instances.bin", out var tieBytes)
            ? UyaTieInstancesReader.Read(tieBytes).Instances : [];
        var meshes = new Dictionary<int, IReadOnlyList<TexturedSurfaceTriangle>>();
        var tieDefinitions = tieDefs.ToDictionary(definition => definition.ModelId);
        for (var index = 0; index < ties.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = ties[index]; var key = $"tie:instance:{index}"; var group = Group(key);
            currentSource = Source(key, "tie", instance.ClassId); sourceFace = 0;
            if (!meshes.TryGetValue(instance.ClassId, out var mesh))
            {
                var definition = tieDefinitions[instance.ClassId];
                var bytes = LevelAssetReader.ReadAssetSlice(assets, definition.ModelOffset, offsets);
                var tie = TieClassReader.Read(bytes, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
                var selectedLod = UyaOcclusionMeshLods.SelectLod0(tie.LodTopologies.Select(lod => (lod.LodIndex, lod.Triangles.Count)));
                mesh = TieSurfaceMeshExtractor.ExtractTextured(tie, selectedLod);
                meshes.Add(instance.ClassId, mesh);
            }
            foreach (var triangle in mesh)
            {
                var texture = textureSource.Resolve("tie", triangle.MaterialIndex, tieDefinitions[instance.ClassId].TextureIds);
                var surface = TransformSurface(triangle, instance.Transform);
                Add(surface.A, surface.B, surface.C, group, surface.UvA, surface.UvB, surface.UvC,
                    texture, surface.ClampU, surface.ClampV);
            }
            if (mesh.Count == 0) Force(key); else geometrySources.Add(key);
        }
        IReadOnlyList<UyaShrubInstance> shrubs = [];
        if (files.TryGetValue("gameplay/core/shrub_instances.bin", out var shrubBytes))
        {
            if (shrubBytes.Length < 4 || System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(shrubBytes) > 100_000)
                throw new InvalidDataException("Occlusion shrub instance count exceeds the bounded limit.");
            shrubs = UyaShrubInstancesReader.Read(shrubBytes).Instances;
        }
        var shrubDefinitions = shrubDefs.ToDictionary(definition => definition.ModelId);
        var shrubMeshes = new Dictionary<int, IReadOnlyList<TexturedSurfaceTriangle>>();
        foreach (var (instance, index) in shrubs.Select((instance, index) => (instance, index)))
        {
            currentSource = Source($"shrub:instance:{index}", "shrub", instance.ClassId); sourceFace = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var definition = shrubDefinitions[instance.ClassId];
            if (!shrubMeshes.TryGetValue(instance.ClassId, out var mesh))
            {
                using var stream = new MemoryStream(LevelAssetReader.ReadAssetSlice(assets, definition.ModelOffset, offsets), writable: false);
                var shrub = ShrubClassReader.Read(stream);
                mesh = ShrubSurfaceMeshExtractor.ExtractTextured(shrub);
                shrubMeshes.Add(instance.ClassId, mesh);
            }
            foreach (var triangle in mesh)
            {
                var texture = textureSource.Resolve("shrub", triangle.MaterialIndex, definition.TextureIds);
                var surface = TransformSurface(triangle, instance.Transform);
                Add(surface.A, surface.B, surface.C, -1, surface.UvA, surface.UvB, surface.UvC,
                    texture, surface.ClampU, surface.ClampV);
            }
        }

        var mobys = !coverageOnly && files.TryGetValue("gameplay/core/moby_instances.bin", out var mobyBytes)
            ? UyaMobyInstancesReader.Read(mobyBytes).Instances : [];
        var mobyDefinitions = mobyDefs.ToDictionary(definition => definition.ModelId);
        var mobyHeaders = new Dictionary<int, MobyModel>();
        var mobySpheres = new List<UyaOcclusionBounds>();
        foreach (var link in sources?.Links.Where(link => link.Family == "moby") ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = int.Parse(link.SourceKey.AsSpan("moby:instance:".Length)); var instance = mobys[index];
            var source = Source(link.SourceKey, "moby", instance.ClassId);
            if (!mobyHeaders.TryGetValue(instance.ClassId, out var model))
            {
                using var stream = new MemoryStream(LevelAssetReader.ReadAssetSlice(assets, mobyDefinitions[instance.ClassId].ModelOffset, offsets), writable: false);
                model = MobyModelReader.ReadHeader(stream); mobyHeaders.Add(instance.ClassId, model);
            }
            mobySpheres.Add(UyaOcclusionMobyBounds.Transform(model, instance, source, link.BitIndex));
            Force(link.SourceKey);
        }
        if (!coverageOnly && geometrySources.Count + forcedSources.Count != sources!.Links.Count)
            throw new InvalidDataException("Occlusion scene did not account for every native source object.");
        var gridBytes = coverageOnly ? [] : LevelAssetReader.ReadAssetSlice(assets, h.OcclusionOffset, offsets);
        UyaOcclusionAlphaSampling.Validate(triangles, textureSource.Textures);
        return new(triangles, textureSource.Textures, mobySpheres, sourceRecords, gridBytes,
            coverageOnly ? [] : UyaOcclusionOceanPlanes.ReadHeights(files, cancellationToken));
    }

    internal static TexturedSurfaceTriangle TransformSurface(TexturedSurfaceTriangle triangle, UyaInstanceTransform transform)
    {
        var x = transform.BasisX; var y = transform.BasisY; var z = transform.BasisZ;
        var mirrored = (double)x.X * ((double)y.Y * z.Z - (double)y.Z * z.Y)
            + (double)x.Y * ((double)y.Z * z.X - (double)y.X * z.Z)
            + (double)x.Z * ((double)y.X * z.Y - (double)y.Y * z.X) < 0;
        // Reflection reverses winding; keep front/back qualification and texture coordinates consistent.
        return triangle with
        {
            A = World(triangle.A, transform),
            B = World(mirrored ? triangle.C : triangle.B, transform),
            C = World(mirrored ? triangle.B : triangle.C, transform),
            UvB = mirrored ? triangle.UvC : triangle.UvB,
            UvC = mirrored ? triangle.UvB : triangle.UvC,
        };
    }

    private static Vector3 World(Vector3 v, UyaInstanceTransform t) => new(
        t.Position.X + v.X * t.BasisX.X + v.Y * t.BasisY.X + v.Z * t.BasisZ.X,
        t.Position.Y + v.X * t.BasisX.Y + v.Y * t.BasisY.Y + v.Z * t.BasisZ.Y,
        t.Position.Z + v.X * t.BasisX.Z + v.Y * t.BasisY.Z + v.Z * t.BasisZ.Z);
}
