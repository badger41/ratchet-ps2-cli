using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Games.UYA.Collision;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.GC.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.GC.Level;

public static class GcLevelContentInventoryReader
{
    private const int MaximumItems = 100_000;

    public static IReadOnlyList<LevelContentFamily> Read(byte[] levelWad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levelWad);
        cancellationToken.ThrowIfCancellationRequested();
        var wad = UyaLevelWadReader.ReadLevelWad(levelWad);
        var files = UyaLevelWadUnpacker.UnpackLevelData(
            UyaLevelWadReader.ReadSectorFileBlock(levelWad, wad.Data));
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(files);
        var header = LevelAssetReader.ReadHeader(source.HeaderBytes);
        var assets = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes, new(), cancellationToken) : source.AssetWadBytes;
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(header, assets.Length,
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.MobyModelOffset, header.MobyModelCount),
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.TieModelOffset, header.TieModelCount),
            LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, header.ShrubModelOffset, header.ShrubModelCount),
            [header.SceneViewSize]);
        var storedGameplay = UyaLevelWadReader.ReadSectorFileBlock(levelWad, wad.Gameplay);
        var gameplay = BinaryMagic.IsWad(storedGameplay)
            ? WadCompression.Decompress(storedGameplay, new(), cancellationToken) : storedGameplay;
        var blocks = gameplay is { Length: > 0 }
            ? GameplayLayoutReader.Read(gameplay, GcGameplayLayout.Core).Blocks : [];
        var families = new List<LevelContentFamily>();

        AddInstances("ties", "tie_instances", bytes => UyaTieInstancesReader.Read(bytes).Instances
            .Select((value, index) => Item("ties", "tie_instances", index, value.ClassId)).ToArray());
        AddInstances("shrubs", "shrub_instances", bytes => UyaShrubInstancesReader.Read(bytes).Instances
            .Select((value, index) => Item("shrubs", "shrub_instances", index, value.ClassId)).ToArray());
        AddInstances("mobys", "moby_instances", bytes => UyaMobyInstancesReader.Read(bytes).Instances
            .Select((value, index) => Item("mobys", "moby_instances", index, value.ClassId)).ToArray());
        AddInstances("cuboids", "cuboids", bytes => Indexed("cuboids", GameplayGeometryReader.ReadCuboids(bytes).Length));
        AddInstances("spheres", "spheres", bytes => Indexed("spheres", GameplayGeometryReader.ReadShapes(bytes, "sphere").Length));
        AddInstances("splines", "splines", bytes => Indexed("splines", GameplayGeometryReader.ReadSplines(bytes).Length));
        AddInstances("paths", "grind_splines", bytes => Indexed("paths", GameplayGeometryReader.ReadGrindPaths(bytes).Length));
        AddInstances("areas", "areas", bytes => Indexed("areas", GameplayGeometryReader.ReadAreas(bytes).Length));
        AddInstances("directionalLights", "directional_lights", bytes => Indexed("directionalLights",
            UyaGameplayLightingReader.ReadDirectionalLights(bytes).Length));
        AddInstances("levelSettings", "level_settings", bytes =>
        {
            _ = GcLevelSettingsReader.Read(bytes);
            return [new("source:levelSettings:singleton")];
        });
        Add("mapGroups", blocks.Any(block => block.Pointer > 0 && block.SemanticName.EndsWith("_groups")),
            blocks.Where(block => block.SemanticName.EndsWith("_groups")).Sum(block => (long)block.PayloadBytes.Length), () =>
        {
            var items = new List<LevelContentItem>();
            foreach (var (section, kind) in new[] { ("moby_groups", "moby"), ("tie_groups", "tie"), ("shrub_groups", "shrub") })
            {
                var bytes = Payload(section);
                if (bytes.Length == 0) continue;
                var groups = UyaTieGroupsReader.Read(bytes).Groups;
                items.Add(new($"source:mapGroups:{kind}:table", ItemCount: 0));
                items.AddRange(groups.Select((_, index) => new LevelContentItem($"source:mapGroups:{kind}:{index}")));
            }
            return items.ToArray();
        });

        var terrain = new List<(string Owner, byte[] Bytes)>();
        var collision = new List<(string Owner, byte[] Bytes)>();
        terrain.Add(("primary:0", LevelAssetReader.ReadAssetSlice(assets, header.TerrainOffset, offsets, allowZeroOffset: true)));
        collision.Add(("primary:0", LevelAssetReader.ReadAssetSlice(assets, header.CollisionOffset, offsets)));
        // Slot zero mirrors the primary owner and must not be counted twice.
        for (var index = 1; index < wad.Chunks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = UyaLevelWadReader.ReadSectorFileBlock(levelWad, wad.Chunks[index]);
            if (bytes.Length == 0) continue;
            terrain.Add(($"chunk:{index}", TfragChunkWadReader.ReadTerrainPayload(bytes)));
            collision.Add(($"chunk:{index}", TfragChunkWadReader.ReadCollisionPayload(bytes)));
        }
        Add("tfrags", terrain.Any(value => value.Bytes.Length > 0), terrain.Sum(value => (long)value.Bytes.Length), () =>
            terrain.Where(value => value.Bytes.Length > 0 && value.Bytes.AsSpan().ContainsAnyExcept((byte)0))
                .Select(value => new LevelContentItem($"source:tfrags:{value.Owner}",
                    ItemCount: TfragTerrainReader.Read(value.Bytes).TfragCount)).Where(value => value.ItemCount > 0).ToArray());
        Add("collision", collision.Any(value => value.Bytes.Length > 0), collision.Sum(value => (long)value.Bytes.Length), () =>
            collision.Where(value => value.Bytes.Length > 0).SelectMany(value =>
            {
                var mesh = UyaCollisionReader.Read(value.Bytes);
                return mesh.SolidPieces.Select(piece => new LevelContentItem($"source:collision:{value.Owner}:solid:{piece.SourceIndex}"))
                    .Concat(mesh.PlayerBarriers.Select(piece => new LevelContentItem($"source:collision:{value.Owner}:barrier:{piece.SourceIndex}")));
            }).ToArray());
        var sky = LevelAssetReader.ReadAssetSlice(assets, header.SkyOffset, offsets);
        Add("skybox", sky.Length > 0, sky.Length, () =>
        {
            if (sky.Length == 0) return [];
            using var stream = new MemoryStream(sky, writable: false);
            return SkyboxReader.Read(stream, GameId.GC).Shells.Select(shell =>
                new LevelContentItem($"source:skybox:{shell.Index}")).ToArray();
        });
        var hud = files.Where(file => file.Path.StartsWith("hud/", StringComparison.Ordinal)).ToArray();
        Add("hud", hud.Any(file => file.Bytes.Length > 0), hud.Sum(file => (long)file.Bytes.Length), () =>
            hud.Any(file => file.Bytes.Length > 0) ? [new("source:hud:singleton")] : []);
        Add("fx", header.FxTextureCount > 0, 0, () => header.FxTextureCount > 0 ? [new("source:fx:singleton")] : []);
        var occlusion = Payload("occlusion");
        Add("occlusion", occlusion.Length > 0, occlusion.Length, () =>
            occlusion.Length > 0 ? [new("source:occlusion:singleton")] : []);
        Add("opaqueContent", false, 0, () => []);
        return families;

        byte[] Payload(string section) => blocks.SingleOrDefault(block => block.SemanticName == section)?.PayloadBytes ?? [];

        void AddInstances(string family, string section, Func<byte[], LevelContentItem[]> read)
        {
            var bytes = Payload(section);
            Add(family, blocks.Any(block => block.SemanticName == section && block.Pointer > 0), bytes.Length,
                () => bytes.Length == 0 ? [] : read(bytes));
        }

        void Add(string family, bool present, long size, Func<LevelContentItem[]> read)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var items = read();
                if (items.Length > MaximumItems || items.Sum(item => (long)item.ItemCount) > MaximumItems)
                    throw new InvalidDataException("Content inventory exceeds the item limit.");
                families.Add(new(family, present, items, size));
            }
            catch (Exception cause) when (cause is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException)
            {
                var diagnostic = $"GC {family}: {cause.Message}";
                families.Add(new(family, present, [], size, diagnostic.Length > 512 ? diagnostic[..512] : diagnostic));
            }
        }
    }

    private static LevelContentItem Item(string family, string section, int index, int? classId = null) =>
        new($"source:{family}:gameplay/core/{section}:{index}", classId);

    private static LevelContentItem[] Indexed(string family, int count) => Enumerable.Range(0, count)
        .Select(index => new LevelContentItem($"source:{family}:{index}")).ToArray();
}
