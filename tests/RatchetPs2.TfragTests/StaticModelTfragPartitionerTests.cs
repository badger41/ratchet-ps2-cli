using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Tfrags;
using RatchetPs2.Sdk;
using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class StaticModelTfragPartitionerTests
{
    public static void Run()
    {
        var asset = GridAsset(30);
        var first = StaticModelTfragConverter.Partition(GameId.UYA, asset);
        var second = StaticModelTfragConverter.Partition(GameId.UYA, asset);
        var sourceTriangleCount = asset.Primitives.Sum(value => value.Indices.Length / 3);
        var sourceTriangles = first.SelectMany(partition => partition.Primitives)
            .SelectMany(primitive => primitive.SourceTriangleIndices)
            .Order()
            .ToArray();
        Expect(first.Count > 1
            && first.Sum(value => value.TriangleCount) == sourceTriangleCount
            && sourceTriangles.SequenceEqual(Enumerable.Range(0, sourceTriangleCount)),
            "tfrag partitioning should retain every source triangle exactly once");
        Expect(first.All(value =>
                value.VertexCount <= UyaStaticModelTfragPartitioner.MaximumVerticesPerPartition
                && value.TriangleCount <= UyaStaticModelTfragPartitioner.MaximumTrianglesPerPartition
                && value.SourceMaterialIndices.Count
                    <= UyaStaticModelTfragPartitioner.MaximumMaterialsPerPartition
                && MaximumAbsoluteLocalPosition(value)
                    <= UyaStaticModelTfragPartitioner.MaximumLocalCoordinate),
            "tfrag partitions should stay inside the observed UYA native group limits");
        Expect(first.All(value =>
                value.SourceBounds.MaximumX - value.SourceBounds.MinimumX < 20
                || value.SourceBounds.MaximumY - value.SourceBounds.MinimumY < 20),
            "tfrag partitions should remain spatially local instead of slicing only by source order");
        Expect(Signature(first) == Signature(second),
            "tfrag spatial partitioning should be deterministic");
        Expect(first.SelectMany(value => value.Primitives).All(value =>
                value.Geometry.Positions.Length / 3
                    == value.Geometry.Normals!.Length / 3
                && value.Geometry.Positions.Length / 3
                    == value.Geometry.TexCoords!.Length / 2),
            "tfrag partitioning should remap every retained vertex attribute");
        var compactAsset = GridAsset(7);
        var compactPartitions = StaticModelTfragConverter.Partition(GameId.UYA, compactAsset);
        var compactPieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA, compactAsset, compactPartitions, new Dictionary<int, int> { [0] = 3 });
        var compactTerrain = TfragTerrainReader.Read(
            StaticModelTfragConverter.BuildTerrain(GameId.UYA, compactPieces));
        Expect(compactPartitions.Count == 1 && compactPartitions[0].TriangleCount == 98
            && compactTerrain.Chunks.Count == 1,
            "connected meshes that fit the native shared-vertex buffer should remain in one tfrag group");
        var vuPressureAsset = VuPressureAsset();
        var vuPressurePartitions = StaticModelTfragConverter.Partition(GameId.UYA, vuPressureAsset);
        var vuPressurePieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            vuPressureAsset,
            vuPressurePartitions,
            Enumerable.Range(0, 8).ToDictionary(index => index, _ => 3));
        _ = StaticModelTfragConverter.BuildTerrain(GameId.UYA, vuPressurePieces);
        Expect(vuPressurePartitions.Count > 1,
            "tfrag partitioning should split real strip layouts that exceed the native VU buffer");

        var textureConversion = StaticModelTfragConverter.ConvertTextures(GameId.UYA, asset);
        Expect(textureConversion.Textures.Count == 1
            && textureConversion.Materials is [{ SourceMaterialIndex: 0 }]
            && textureConversion.Materials[0].TextureAssetId == textureConversion.Textures[0].AssetId,
            "tfrag texture preparation should preserve material identity without invoking TIE mesh limits");
        var sharedNativeTextureConversion = StaticModelTfragConverter.ConvertTextures(
            GameId.UYA, QuantizedDuplicateTextureAsset());
        Expect(sharedNativeTextureConversion.Textures.Count == 1
            && sharedNativeTextureConversion.Materials.Count == 2
            && sharedNativeTextureConversion.Materials.Select(value => value.TextureAssetId)
                .Distinct(StringComparer.Ordinal).Single()
                == sharedNativeTextureConversion.Textures[0].AssetId,
            "textures that quantize to the same native PIF should share one content-addressed asset");
        var oversizedTextureAsset = asset with
        {
            Images = [new("terrain", 256, 256, Enumerable.Repeat((byte)255, 256 * 256 * 4).ToArray())],
        };
        var resizedConversion = StaticModelTfragConverter.ConvertTextures(GameId.UYA, oversizedTextureAsset);
        var resizedTexture = resizedConversion.Textures.Single();
        Expect(resizedTexture.Width == UyaStaticModelTfragPartitioner.MaximumTextureDimension
            && resizedTexture.Height == UyaStaticModelTfragPartitioner.MaximumTextureDimension
            && resizedConversion.Diagnostics.Any(value => value.Code == "texture-resized"),
            "tfrag texture preparation should fit dynamic mip levels to the native terrain ceiling");
        var tiledAsset = GridAsset(1) with
        {
            Primitives =
            [
                TrianglePrimitive(0, [0, 0, 0, 1, 0, 0, 0, 1, 0]) with
                {
                    TexCoords = [128, 64, 132, 64, 128, 68],
                },
            ],
            Bounds = new(0, 0, 0, 1, 1, 0),
        };
        var tiledPieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            tiledAsset,
            StaticModelTfragConverter.Partition(GameId.UYA, tiledAsset),
            new Dictionary<int, int> { [0] = 3 });
        var tiledTexCoords = tiledPieces.Single().Asset.Geometry.Lods[0].Primitives.Single().Vertices
            .Select(value => value.TexCoord).ToArray();
        Expect(tiledTexCoords.All(value => value.X is >= -4 and < 8 && value.Y is >= -4 and < 8)
            && MathF.Abs(tiledTexCoords.Max(value => value.X) - tiledTexCoords.Min(value => value.X) - 4) < 0.001f
            && MathF.Abs(tiledTexCoords.Max(value => value.Y) - tiledTexCoords.Min(value => value.Y) - 4) < 0.001f,
            "large repeated UV offsets should be rebased without collapsing deliberate multi-tile spans");
        _ = StaticModelTfragConverter.BuildTerrain(GameId.UYA, tiledPieces);
        var expandedUvAsset = GridAsset(1) with
        {
            Primitives =
            [
                GridAsset(1).Primitives[0] with
                {
                    TexCoords = [128, 64, 132, 64, 128, 68, 132, 68],
                },
            ],
        };
        var expandedUvPartitions = StaticModelTfragConverter.Partition(GameId.UYA, expandedUvAsset);
        var expandedUvPieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            expandedUvAsset,
            expandedUvPartitions,
            new Dictionary<int, int> { [0] = 3 });
        Expect(expandedUvPartitions.Single().VertexCount == 6
            && expandedUvPieces.Single().Asset.Geometry.Lods[0].Primitives.Single().Vertices.Count == 6,
            "native UV expansion should happen before partition limits are measured");
        var denseTextureConversion = StaticModelTfragConverter.ConvertTextures(
            GameId.UYA, MultiTextureAsset(40));
        Expect(denseTextureConversion.Textures.Count == 40
            && denseTextureConversion.Textures.All(value => value.Width == 64 && value.Height == 64)
            && denseTextureConversion.Diagnostics.Any(value => value.Code == "texture-streaming-budget"),
            "large tfrag texture sets should downscale together to fit the native GS streaming window");
        var denseAsset = MultiTextureAsset(40);
        var densePieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            denseAsset,
            StaticModelTfragConverter.Partition(GameId.UYA, denseAsset),
            denseTextureConversion,
            denseTextureConversion.Textures.Select((texture, index) =>
                new TerrainTextureAllocation(texture.AssetId, index + 10)).ToArray());
        Expect(densePieces.All(piece => piece.StreamingTextureIds
                .SequenceEqual(piece.Asset.Geometry.TextureIds)),
            "every generated piece should stream only the textures used by that partition");
        var denseTerrainBytes = StaticModelTfragConverter.BuildTerrain(GameId.UYA, densePieces);
        var denseTerrain = TfragTerrainReader.Read(denseTerrainBytes);
        Expect(denseTerrain.Chunks.Zip(densePieces).All(pair =>
                pair.First.MSphereCount == pair.Second.StreamingTextureIds.Count
                && Enumerable.Range(0, pair.First.MSphereCount).Select(index =>
                    (int)denseTerrainBytes[pair.First.DataOffset + pair.First.MSphereOffset
                        + index * 0x10 + 0x0f])
                    .SequenceEqual(pair.Second.StreamingTextureIds)),
            "every native piece should advertise only its own texture set to the runtime streamer");
        StaticModelTfragConverter.ValidateTextureAllocation(GameId.UYA, 216, 40);
        ExpectThrows<InvalidDataException>(() =>
            StaticModelTfragConverter.ValidateTextureAllocation(GameId.UYA, 217, 40));
        ExpectThrows<InvalidDataException>(() =>
            StaticModelTfragConverter.ValidateTextureAllocation(GameId.UYA, 0, 256));

        var pieces = StaticModelTfragConverter.BuildPieces(GameId.UYA, asset, first,
            new Dictionary<int, int> { [0] = 3 });
        Expect(pieces.Count == first.Count
            && pieces.Select(value => value.PartitionIndex).SequenceEqual(Enumerable.Range(0, first.Count))
            && pieces.All(value => value.Asset.Geometry.Lods[0].Primitives.Count > 0
                && value.Asset.Geometry.Lods[1].Primitives.Count == 0
                && value.Asset.Geometry.Lods[2].Primitives.Count == 0
                && value.Asset.CanonicalBytes.SequenceEqual(
                    RatchetPs2.Core.Tfrags.TfragPieceCanonicalCodec.Write(value.Asset.Geometry))),
            "tfrag construction should produce deterministic LOD0-only canonical pieces");
        var allocatedPieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            asset,
            first,
            textureConversion,
            [new(textureConversion.Textures[0].AssetId, 7)]);
        Expect(allocatedPieces.All(piece => piece.Asset.Geometry.TextureIds.SequenceEqual([7])),
            "tfrag construction should bind converted material identities to allocated terrain texture IDs");
        var overriddenBindings = textureConversion with
        {
            Materials = textureConversion.Materials.Select(value => value with
            {
                ClampU = true,
                ClampV = true,
            }).ToArray(),
        };
        var clampPieces = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            asset,
            first,
            overriddenBindings,
            [new(textureConversion.Textures[0].AssetId, 7)]);
        Expect(clampPieces.All(piece => piece.Asset.Geometry.Lods[0].Primitives
                .All(primitive => primitive.Material.ClampU && primitive.Material.ClampV)),
            "tfrag material construction should consume converted clamp policy without reinterpreting glTF samplers");
        var terrainBytes = StaticModelTfragConverter.BuildTerrain(GameId.UYA, pieces);
        var terrainHash = Convert.ToHexString(SHA256.HashData(terrainBytes));
        Expect(UyaStaticModelTfragWriter.GenerationVersion == 19
            && terrainHash == "1F78608D54FCBEFD4F741E97C577A590D7A05E88EA846EAB1AC59F6BF5462708",
            $"native tfrag fixture changed without a generation-version update: {terrainHash}");
        var terrain = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(terrainBytes);
        var sourcePieceTriangleCount = pieces.Sum(piece => piece.Asset.Geometry.Lods[0].Primitives
            .Sum(primitive => primitive.Indices.Count / 3));
        Expect(terrain.Chunks.Count == pieces.Count
            && terrain.Chunks.Sum(value => value.TriangleCount) >= sourcePieceTriangleCount
            && terrain.Chunks.Sum(value => value.TriangleCount) <= sourcePieceTriangleCount * 2
            && terrain.Chunks.Zip(pieces).All(pair => pair.First.VertexCount
                == AlignFour(pair.Second.Asset.Geometry.Lods[0].Primitives.Sum(primitive => primitive.Vertices.Count)))
            && terrain.Chunks.All(value => value.BaseOnly == 1 && value.TextureEntries.Count == 1
                && value.TextureEntries[0].TextureId == 3),
            "tfrag native writing should preserve the partition, texture inventory, and shared vertices");
        Expect(terrain.Chunks.All(chunk => chunk.MSphereCount == 1
                && Enumerable.Range(0, chunk.MSphereCount).All(index =>
                    BinaryPrimitives.ReadUInt16LittleEndian(terrainBytes.AsSpan(
                        chunk.DataOffset + chunk.MSphereOffset + index * 0x10 + 0x0e, 2)) == 0x03ff)),
            "generated base-only tfrags should emit mesh spheres with their terrain texture streaming ID");
        Expect(terrain.Chunks.All(chunk => chunk.RgbaEntries.All(color => color.A == 128)),
            "generated opaque tfrags should use the native fully-opaque alpha value");
        Expect(terrain.Chunks.All(chunk => Enumerable.Range(0, chunk.VertexCount).All(index =>
                (BinaryPrimitives.ReadUInt16LittleEndian(terrainBytes.AsSpan(
                    chunk.DataOffset + chunk.LightOffset + 0x10 + index * 8 + 4, 2)) & 0x8000) != 0)),
            "generated opaque tfrag light colors should set the native 5:5:5:1 alpha bit");
        Expect(terrain.Chunks.All(chunk => Enumerable.Range(0, chunk.VertexCount).All(index =>
                BinaryPrimitives.ReadUInt16LittleEndian(terrainBytes.AsSpan(
                    chunk.DataOffset + chunk.LightOffset + 0x10 + index * 8 + 4, 2)) == 0x98c6
                && terrainBytes[chunk.DataOffset + chunk.LightOffset + 0x10 + index * 8 + 1] == 4)),
            "generated tfrags should use the observed neutral native light base and post-scale");
        Expect(terrain.Chunks.All(chunk =>
        {
            var header = terrainBytes.AsSpan(chunk.DataOffset + chunk.SharedOffset + 4, 0x28);
            var vertexInfoEnd = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1a..]);
            return BinaryPrimitives.ReadUInt16LittleEndian(header[0x10..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x12..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x14..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x16..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x18..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x1c..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x1e..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x20..]) == vertexInfoEnd
                && BinaryPrimitives.ReadUInt16LittleEndian(header[0x22..]) == vertexInfoEnd;
        }), "generated base-only tfrags should populate every VU header address");
        Expect(terrain.Chunks.All(chunk => MaximumVerticesBetweenKicks(terrainBytes, chunk) <= 24),
            "generated independent-row tfrag packets should reserve near-plane clipping headroom");
        Expect(terrain.Chunks.All(chunk => HasTerminatedGsPackets(terrainBytes, chunk)),
            "generated tfrag GS packets should terminate before the strip sentinel");
        Expect(terrain.Chunks.All(chunk => StripRowCount(terrainBytes, chunk)
                <= UyaStaticModelTfragPartitioner.MaximumTrianglesPerPartition),
            "generated base-only tfrags should stay within the retail strip-control row ceiling");
        Expect(terrain.Chunks.All(chunk => HasNativeStripRows(terrainBytes, chunk)),
            "generated base-only tfrags should use even paired-triangle strip rows");
        Expect(terrain.Chunks.All(chunk =>
                BinaryPrimitives.ReadUInt16LittleEndian(terrainBytes.AsSpan(chunk.RecordOffset + 0x3e, 2))
                    == ushort.MaxValue),
            "generated base-only tfrags should stay on the shared high-detail topology");
        var rotated = compactPieces.Single() with
        {
            Transform = compactPieces.Single().Transform with
            {
                Position = new(12, 34, 56),
                Rotation = new(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4)),
                Scale = new(2, 2, 2),
            },
        };
        var transformedBytes = StaticModelTfragConverter.BuildTerrain(GameId.UYA, [rotated]);
        var transformedTerrain = TfragTerrainReader.Read(transformedBytes);
        var transformedChunk = transformedTerrain.Chunks.Single();
        var transformedGeometry = TfragPieceConverter.Extract(
            [new("primary.bin", TfragPieceSourceKind.Primary, 0, transformedBytes)],
            GameId.UYA).Assets.Single().Geometry;
        var expectedGeometry = TfragPieceConverter.Transform(
            rotated.Asset.Geometry,
            rotated.Transform with { Position = new(0, 0, 0) });
        Expect(MathF.Abs(transformedChunk.BoundingSphere.X - 12 * 1024) < 0.01f
            && MathF.Abs(transformedChunk.BoundingSphere.Y - 34 * 1024) < 0.01f
            && MathF.Abs(transformedChunk.BoundingSphere.Z - 56 * 1024) < 0.01f
            && MathF.Abs(transformedChunk.BoundingSphere.Radius
                - rotated.Asset.Geometry.Bounds.Radius * 2 * 1024) < 1f
            && Near(expectedGeometry.Bounds.Minimum, transformedGeometry.Bounds.Minimum, 0.002f)
            && Near(expectedGeometry.Bounds.Maximum, transformedGeometry.Bounds.Maximum, 0.002f),
            "generated tfrags should bake positive-uniform scale and rotation into native geometry");
        var oversizedTransform = compactPieces.Single() with
        {
            Transform = compactPieces.Single().Transform with { Scale = new(10, 10, 10) },
        };
        var oversizedGroupCount = StaticModelTfragConverter.GetNativeTerrainGroupCount(
            GameId.UYA, oversizedTransform);
        var oversizedTransformBytes = StaticModelTfragConverter.BuildTerrain(
            GameId.UYA, [oversizedTransform]);
        var oversizedTransformTerrain = TfragTerrainReader.Read(oversizedTransformBytes);
        var oversizedExtraction = TfragPieceConverter.Extract(
            [new("primary.bin", TfragPieceSourceKind.Primary, 0, oversizedTransformBytes)],
            GameId.UYA);
        var oversizedAssets = oversizedExtraction.Assets.ToDictionary(value => value.ContentId);
        Expect(oversizedGroupCount > 1
            && oversizedTransformTerrain.TfragCount == oversizedGroupCount
            && oversizedExtraction.Occurrences.Sum(occurrence =>
                oversizedAssets[occurrence.ContentId].Geometry.Lods[0].Primitives
                    .Sum(primitive => primitive.Indices.Count / 3))
                == compactPieces.Single().Asset.Geometry.Lods[0].Primitives
                    .Sum(primitive => primitive.Indices.Count / 3),
            "scaled tfrags should subdivide into native groups without losing triangles");
        var largeTriangleAsset = asset with
        {
            Primitives = [TrianglePrimitive(0, [0, 0, 0, 40, 0, 0, 0, 40, 0])],
            Bounds = new(0, 0, 0, 40, 40, 0),
        };
        var sourceLargeTrianglePiece = StaticModelTfragConverter.BuildPieces(
            GameId.UYA,
            largeTriangleAsset,
            StaticModelTfragConverter.Partition(GameId.UYA, largeTriangleAsset),
            new Dictionary<int, int> { [0] = 3 }).Single();
        var largeTrianglePiece = sourceLargeTrianglePiece with
        {
            Transform = sourceLargeTrianglePiece.Transform with { Scale = new(2, 2, 2) },
        };
        var tessellatedGroupCount = StaticModelTfragConverter.GetNativeTerrainGroupCount(
            GameId.UYA, largeTrianglePiece);
        var tessellatedTerrain = TfragTerrainReader.Read(
            StaticModelTfragConverter.BuildTerrain(GameId.UYA, [largeTrianglePiece]));
        Expect(tessellatedGroupCount > 1 && tessellatedTerrain.TfragCount == tessellatedGroupCount,
            "a scaled triangle wider than native position storage should tessellate into encodable groups");
        var sourceMappings = terrain.Chunks.Select((_, index) =>
            new UyaOcclusionMapping(index, 1000 + index)).ToArray();
        var spatialMappings = UyaOcclusionMappingsWriter.MapTfragsByNearestSource(
            terrain.Chunks, sourceMappings, terrain.Chunks.Reverse().ToArray());
        Expect(spatialMappings.Select(value => value.OcclusionId)
                .SequenceEqual(sourceMappings.Reverse().Select(value => value.OcclusionId)),
            "custom tfrags should inherit visibility from the nearest native terrain group");
        var remappedBytes = TfragTerrainComposer.RemapTerrainTextures(
            GameId.UYA,
            terrainBytes,
            new Dictionary<int, IReadOnlyDictionary<int, int>>
            {
                [terrain.Chunks.Count - 1] = new Dictionary<int, int> { [3] = 11 },
            });
        var remapped = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(remappedBytes);
        Expect(remapped.Chunks[^1].TextureEntries.All(value => value.TextureId == 11)
            && Enumerable.Range(0, remapped.Chunks[^1].MSphereCount).All(index =>
                remappedBytes[remapped.Chunks[^1].DataOffset + remapped.Chunks[^1].MSphereOffset
                    + index * 0x10 + 0x0f] == 11)
            && remappedBytes.AsSpan(terrain.Chunks[0].DataOffset, terrain.Chunks[0].DataLength)
                .SequenceEqual(terrainBytes.AsSpan(terrain.Chunks[0].DataOffset, terrain.Chunks[0].DataLength)),
            "tfrag texture remapping should update VIF and mesh-sphere references only in selected groups");
        var allocatedBytes = TfragTerrainComposer.FinalizeTerrain(
            GameId.UYA,
            terrainBytes,
            new Dictionary<int, IReadOnlyDictionary<int, string>>
            {
                [terrain.Chunks.Count - 1] = new Dictionary<int, string> { [3] = "texture:orb" },
            },
            [new TerrainTextureAllocation("texture:orb", 11)],
            firstOcclusionMappingIndex: null);
        Expect(allocatedBytes.SequenceEqual(remappedBytes),
            "tfrag texture finalization should use the IDs returned by terrain texture allocation");
        var sourceTerrainBytes = StaticModelTfragConverter.BuildTerrain(GameId.UYA, pieces.Take(1).ToArray());
        var sourceTerrain = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(sourceTerrainBytes);
        var appendedBytes = TfragTerrainComposer.AppendTerrain(
            GameId.UYA, sourceTerrainBytes, pieces.Skip(1).ToArray());
        var appended = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(appendedBytes);
        Expect(appended.TfragCount == pieces.Count
            && sourceTerrainBytes.AsSpan(sourceTerrain.Chunks[0].DataOffset, sourceTerrain.Chunks[0].DataLength)
                .SequenceEqual(appendedBytes.AsSpan(appended.Chunks[0].DataOffset, appended.Chunks[0].DataLength)),
            "tfrag native append should retain source group bytes and add every generated partition");
        var sourceOcclusion = new byte[0x18];
        BinaryPrimitives.WriteInt32LittleEndian(sourceOcclusion, 1);
        BinaryPrimitives.WriteInt32LittleEndian(sourceOcclusion.AsSpan(0x10), 7);
        BinaryPrimitives.WriteInt32LittleEndian(sourceOcclusion.AsSpan(0x14), 70);
        var occlusion = TfragTerrainComposer.ComposeOcclusionMappings(
            GameId.UYA,
            appendedBytes,
            sourceTerrainBytes,
            sourceOcclusion,
            retainedNativeGroupCount: 1,
            visibilityBit: null);
        var composedMappings = UyaOcclusionMappingsReader.Read(occlusion.OcclusionBytes);
        Expect(occlusion.Changed && occlusion.FirstCustomMappingIndex == 1
            && composedMappings.Tfrags.Count == appended.TfragCount
            && composedMappings.Tfrags.Skip(1).All(value => value == composedMappings.Tfrags[0]),
            "tfrag occlusion composition should append nearest native mappings for generated groups");
        var bound = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(
            TfragTerrainComposer.BindMissingOcclusionMappings(GameId.UYA, appendedBytes, 0));
        Expect(bound.Chunks.Select(value => (int)value.OcclusionIndex)
                .SequenceEqual(Enumerable.Range(0, bound.Chunks.Count))
            && bound.Chunks.All(value => value.OcclusionIndexStash == 0),
            "generated tfrags should bind one-to-one to gameplay occlusion mapping slots");
        var finalizedBytes = TfragTerrainComposer.FinalizeTerrain(
            GameId.UYA,
            terrainBytes,
            new Dictionary<int, IReadOnlyDictionary<int, int>>
            {
                [terrain.Chunks.Count - 1] = new Dictionary<int, int> { [3] = 11 },
            },
            0);
        var finalized = TfragTerrainReader.Read(finalizedBytes);
        Expect(finalized.Chunks[^1].TextureEntries.All(value => value.TextureId == 11)
            && finalized.Chunks.Select(value => (int)value.OcclusionIndex)
                .SequenceEqual(Enumerable.Range(0, finalized.Chunks.Count)),
            "one-pass tfrag finalization should remap textures and bind occlusion together");
        var empty = RatchetPs2.Core.Tfrags.TfragTerrainReader.Read(
            TfragTerrainComposer.FilterTerrainGroups(GameId.UYA, appendedBytes, []));
        Expect(empty.TfragCount == 0 && empty.TotalTfragCount == 0
            && empty.ByteLength == RatchetPs2.Core.Tfrags.TfragTerrainReader.TerrainHeaderSize,
            "tfrag filtering should produce a valid empty native terrain payload");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(() =>
            StaticModelTfragConverter.Partition(GameId.UYA, asset, cancellation.Token));
        ExpectThrows<NotSupportedException>(() =>
            StaticModelTfragConverter.Partition(GameId.DL, asset));

        var oversized = GridAsset(1) with
        {
            Primitives = [GridAsset(1).Primitives[0] with
            {
                Positions = GridAsset(1).Primitives[0].Positions.Select(value => value * 3_000f).ToArray(),
            }],
            Bounds = new(0, 0, -750, 3_000, 3_000, 750),
        };
        var normalized = StaticModelTfragConverter.Partition(GameId.UYA, oversized);
        Expect(normalized.All(value => MaximumAbsoluteLocalPosition(value)
                <= UyaStaticModelTfragPartitioner.MaximumLocalCoordinate)
            && MathF.Abs(normalized.Max(value => value.SourceBounds.MaximumX) - 30f) < 0.001f,
            "tfrag partitioning should normalize source units when one triangle cannot fit native storage");

        var collapsible = asset with
        {
            Primitives =
            [
                TrianglePrimitive(0, [0, 0, 0, 3_000, 0, 0, 0, 3_000, 0]),
                TrianglePrimitive(1, [0, 0, 0, 0.02f, 0, 0, 0, 0.02f, 0]),
            ],
            Bounds = new(0, 0, 0, 3_000, 3_000, 0),
        };
        var collapsed = StaticModelTfragConverter.Partition(GameId.UYA, collapsible);
        Expect(collapsed.Sum(value => value.TriangleCount) == 1,
            "tfrag partitioning should discard faces that collapse at native position precision");
        _ = StaticModelTfragConverter.BuildTerrain(
            GameId.UYA,
            StaticModelTfragConverter.BuildPieces(
                GameId.UYA, collapsible, collapsed, new Dictionary<int, int> { [0] = 3 }));
    }

    private static StaticModelPrimitive TrianglePrimitive(int sourcePrimitiveIndex, float[] positions) =>
        new(
            0,
            sourcePrimitiveIndex,
            $"triangle-{sourcePrimitiveIndex}",
            0,
            positions,
            [0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0, 0, 1, 0, 0, 1],
            null,
            [0, 1, 2]);

    private static float MaximumAbsoluteLocalPosition(StaticModelTfragPartition partition) =>
        partition.Primitives.SelectMany(value => value.Geometry.Positions)
            .Max(MathF.Abs);

    private static bool Near(TfragPieceVector3 left, TfragPieceVector3 right, float tolerance) =>
        MathF.Abs(left.X - right.X) <= tolerance
        && MathF.Abs(left.Y - right.Y) <= tolerance
        && MathF.Abs(left.Z - right.Z) <= tolerance;

    private static StaticModelCanonicalAsset GridAsset(int size)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var texCoords = new List<float>();
        for (var y = 0; y <= size; y++)
        {
            for (var x = 0; x <= size; x++)
            {
                positions.AddRange([x, y, MathF.Sin(x * 0.2f) * 0.25f]);
                normals.AddRange([0, 0, 1]);
                texCoords.AddRange([x / (float)size, y / (float)size]);
            }
        }
        var indices = new List<uint>();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var topLeft = checked((uint)(y * (size + 1) + x));
                var topRight = topLeft + 1;
                var bottomLeft = checked((uint)((y + 1) * (size + 1) + x));
                var bottomRight = bottomLeft + 1;
                indices.AddRange([topLeft, topRight, bottomLeft, bottomLeft, topRight, bottomRight]);
            }
        }
        return new(
            StaticModelCanonicalFormat.Version,
            [new(0, 0, "terrain", 0, positions.ToArray(), normals.ToArray(), texCoords.ToArray(), null, indices.ToArray())],
            [new(9_729, 9_729, 10_497, 10_497)],
            [new("terrain", 0, 0)],
            [new("terrain", [1, 1, 1, 1], 0, 1, 1, "OPAQUE", 0.5f, false)],
            [new("terrain", 1, 1, [255, 255, 255, 255])],
            new(0, 0, -0.25f, size, size, 0.25f));
    }

    private static StaticModelCanonicalAsset MultiTextureAsset(int count)
    {
        var primitives = Enumerable.Range(0, count).Select(index =>
            TrianglePrimitive(index, [index * 2, 0, 0, index * 2 + 1, 0, 0, index * 2, 1, 0]) with
            {
                MaterialIndex = index,
            }).ToArray();
        var textures = Enumerable.Range(0, count)
            .Select(index => new StaticModelTexture($"texture-{index}", index, 0))
            .ToArray();
        var materials = Enumerable.Range(0, count)
            .Select(index => new StaticModelMaterial(
                $"material-{index}", [1, 1, 1, 1], index, 1, 1, "OPAQUE", 0.5f, false))
            .ToArray();
        var images = Enumerable.Range(0, count).Select(index =>
        {
            var rgba = new byte[128 * 128 * 4];
            for (var offset = 0; offset < rgba.Length; offset += 4)
            {
                rgba[offset] = checked((byte)(index * 5));
                rgba[offset + 1] = checked((byte)(255 - index * 5));
                rgba[offset + 2] = checked((byte)(index * 3));
                rgba[offset + 3] = 255;
            }
            return new StaticModelImage($"image-{index}", 128, 128, rgba);
        }).ToArray();
        return new(
            StaticModelCanonicalFormat.Version,
            primitives,
            [new(9_729, 9_729, 10_497, 10_497)],
            textures,
            materials,
            images,
            new(0, 0, 0, count * 2, 1, 0));
    }

    private static StaticModelCanonicalAsset QuantizedDuplicateTextureAsset()
    {
        var asset = MultiTextureAsset(2);
        return asset with
        {
            Images = Enumerable.Range(0, 2).Select(imageIndex =>
            {
                var rgba = new byte[16 * 16 * 4];
                for (var index = 0; index < 256; index++)
                {
                    var offset = index * 4;
                    rgba[offset] = checked((byte)index);
                    rgba[offset + 1] = checked((byte)imageIndex);
                    rgba[offset + 3] = 255;
                }
                return new StaticModelImage($"quantized-{imageIndex}", 16, 16, rgba);
            }).ToArray(),
        };
    }

    private static StaticModelCanonicalAsset VuPressureAsset()
    {
        var asset = MultiTextureAsset(8);
        var points = new (float X, float Y)[]
        {
            (0, 0), (1, 0), (1, 1), (0, 1), (-0.5f, 0.5f),
            (0, 2), (1, 2), (1, 3), (0, 3), (-0.5f, 2.5f),
        };
        uint[] baseIndices =
        [
            0, 1, 2, 0, 2, 3, 0, 3, 4,
            5, 6, 7, 5, 7, 8, 5, 8, 9,
        ];
        var primitives = Enumerable.Range(0, 8).Select(index =>
        {
            var positions = points.SelectMany(point => new[]
                { point.X + index * 3, point.Y, 0f }).ToArray();
            var normals = points.SelectMany(_ => new[] { 0f, 0f, 1f }).ToArray();
            var texCoords = points.SelectMany(point => new[] { point.X, point.Y }).ToArray();
            var indices = index < 4 ? [.. baseIndices, 0u, 4u, 5u] : baseIndices;
            return new StaticModelPrimitive(
                0, index, $"vu-pressure-{index}", index,
                positions, normals, texCoords, null, indices);
        }).ToArray();
        return asset with
        {
            Primitives = primitives,
            Bounds = new(-0.5f, 0, 0, 22, 3, 0),
        };
    }

    private static string Signature(IEnumerable<RatchetPs2.Core.Tfrags.StaticModelTfragPartition> partitions) =>
        string.Join('|', partitions.Select(value =>
            $"{value.Origin.X:R},{value.Origin.Y:R},{value.Origin.Z:R}:"
            + string.Join(',', value.Primitives.SelectMany(primitive => primitive.SourceTriangleIndices))));

    private static int MaximumVerticesBetweenKicks(byte[] bytes, RatchetPs2.Core.Tfrags.TfragChunk chunk)
    {
        var data = bytes.AsSpan(chunk.DataOffset, chunk.RgbaOffset);
        var stripsAddress = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(chunk.SharedOffset + 4 + 0x24)..]);
        var packet = RatchetPs2.Core.IO.Vif.Ps2VifPacket.ReadSpans(data)
            .Last(value => value.Command == 0x6e && value.DestinationAddress == stripsAddress);
        var payload = data.Slice(packet.Offset + 4, packet.RawPayloadLength);
        var current = 0;
        var maximum = 0;
        for (var offset = 0; offset + 3 < payload.Length; offset += 4)
        {
            var count = (int)(sbyte)payload[offset];
            var control = (sbyte)payload[offset + 1];
            if (count == 0)
            {
                maximum = Math.Max(maximum, current);
                break;
            }
            if (count < 0)
            {
                count += 128;
            }
            current += count;
            if (control < 0)
            {
                maximum = Math.Max(maximum, current);
                current = 0;
            }
        }
        return maximum;
    }

    private static int AlignFour(int value) => (value + 3) & ~3;

    private static bool HasTerminatedGsPackets(byte[] bytes, RatchetPs2.Core.Tfrags.TfragChunk chunk)
    {
        var data = bytes.AsSpan(chunk.DataOffset, chunk.RgbaOffset);
        var stripsAddress = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(chunk.SharedOffset + 4 + 0x24)..]);
        var packet = RatchetPs2.Core.IO.Vif.Ps2VifPacket.ReadSpans(data)
            .Last(value => value.Command == 0x6e && value.DestinationAddress == stripsAddress);
        var payload = data.Slice(packet.Offset + 4, packet.RawPayloadLength);
        var packetOpen = false;
        for (var row = 0; row < packet.EffectiveNum; row++)
        {
            var count = (sbyte)payload[row * 4];
            if (count == 0) return !packetOpen;
            packetOpen = true;
            if ((sbyte)payload[row * 4 + 1] < 0) packetOpen = false;
        }
        return false;
    }

    private static int StripRowCount(byte[] bytes, RatchetPs2.Core.Tfrags.TfragChunk chunk)
    {
        var data = bytes.AsSpan(chunk.DataOffset, chunk.RgbaOffset);
        var stripsAddress = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(chunk.SharedOffset + 4 + 0x24)..]);
        var packet = RatchetPs2.Core.IO.Vif.Ps2VifPacket.ReadSpans(data)
            .Last(value => value.Command == 0x6e && value.DestinationAddress == stripsAddress);
        var payload = data.Slice(packet.Offset + 4, packet.RawPayloadLength);
        var rows = 0;
        while (rows < packet.EffectiveNum && payload[rows * 4] != 0) rows++;
        return rows;
    }

    private static bool HasNativeStripRows(byte[] bytes, RatchetPs2.Core.Tfrags.TfragChunk chunk)
    {
        var data = bytes.AsSpan(chunk.DataOffset, chunk.RgbaOffset);
        var stripsAddress = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(chunk.SharedOffset + 4 + 0x24)..]);
        var packet = RatchetPs2.Core.IO.Vif.Ps2VifPacket.ReadSpans(data)
            .Last(value => value.Command == 0x6e && value.DestinationAddress == stripsAddress);
        var payload = data.Slice(packet.Offset + 4, packet.RawPayloadLength);
        for (var row = 0; row < packet.EffectiveNum; row++)
        {
            var count = (int)(sbyte)payload[row * 4];
            if (count == 0) return true;
            if (count < 0) count += 128;
            if (count < 4 || (count & 1) != 0) return false;
        }
        return false;
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
