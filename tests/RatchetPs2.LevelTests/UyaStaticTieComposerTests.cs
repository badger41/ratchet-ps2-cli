using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;

internal static class UyaStaticTieComposerTests
{
    public static void Run()
    {
        var asset = SharedTextureAsset();
        var converted = StaticModelConverter.Convert(GameId.UYA, asset, 0x8123);
        var repeated = StaticModelConverter.Convert(GameId.UYA, asset, 0x8123);
        Expect(converted.ModelBytes.SequenceEqual(repeated.ModelBytes)
            && converted.ModelSha256 == repeated.ModelSha256,
            "UYA static TIE conversion should be deterministic");
        Expect(converted.ModelSha256 == "7E8074F585E295DA96C96B601CA8C33121EB002EF929E140F63551AB71649630",
            $"UYA static TIE snapshot changed: {converted.ModelSha256}");
        Expect(converted.Textures.Count == 1
            && converted.Materials.Count == 2
            && converted.Materials.Select(value => value.TextureAssetId).Distinct().Count() == 1,
            "materials using identical effective pixels should share one canonical texture asset");
        Expect(ReferenceEquals(converted.MaterialTexturePifBytes[0], converted.MaterialTexturePifBytes[1]),
            "shared material textures should not be encoded twice");
        var generatedTexture = PifReader.Read(converted.Textures.Single().PifBytes);
        var mipColors = generatedTexture.MipPixelData.Select(level =>
        {
            var paletteIndex = TextureConverter.DecodePaletteIndex(level[0]);
            return generatedTexture.PaletteData.AsSpan(paletteIndex * 4, 4).ToArray();
        }).ToArray();
        Expect(generatedTexture.Header is { USize: 2, VSize: 2, MipLevels: 3 }
            && generatedTexture.MipPixelData.Select(value => value.Length).SequenceEqual([1, 1])
            && mipColors.All(value => value.SequenceEqual(new byte[] { 128, 128, 128, 128 }))
            && generatedTexture.PixelData.All(pixel =>
                generatedTexture.PaletteData[TextureConverter.DecodePaletteIndex(pixel) * 4 + 3] == 128),
            "generated texture PIF should reread with its source dimensions and UYA's two material mip levels");

        var transparentAsset = asset with
        {
            Materials = asset.Materials.Select(material => material with { AlphaMode = "BLEND" }).ToArray(),
            Images = [asset.Images[0] with
            {
                Rgba32 =
                [
                    255, 0, 0, 255,
                    0, 0, 255, 0,
                    0, 0, 255, 0,
                    0, 0, 255, 0,
                ],
            }],
        };
        var transparentTexture = PifReader.Read(StaticModelConverter.Convert(
            GameId.UYA, transparentAsset, 0x8124).Textures.Single().PifBytes);
        var transparentMipColors = transparentTexture.MipPixelData.Select(level =>
        {
            var paletteIndex = TextureConverter.DecodePaletteIndex(level[0]);
            return transparentTexture.PaletteData.AsSpan(paletteIndex * 4, 4).ToArray();
        });
        Expect(transparentMipColors.All(value => value.SequenceEqual(new byte[] { 255, 0, 0, 32 })),
            "transparent texture mips should average premultiplied RGB without bleeding hidden colors");

        Expect(converted.DefinitionBytes.Length == 0x20
            && converted.DefinitionBytes.AsSpan(0x10, 0x10).ToArray().All(value => value == byte.MaxValue),
            "generated UYA TIE definition should reserve all material slots for archive composition");

        var tie = TieClassReader.Read(converted.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(tie.Header.OClass == unchecked((short)0x8123)
            && tie.Header.TClass == 0
            && tie.Header.TextureCount == 2
            && tie.Shaders[0].ClampU
            && !tie.Shaders[0].ClampV
            && !tie.Shaders[1].ClampU
            && tie.Shaders[1].ClampV,
            "semantic reread should retain class, material count, and draw-state clamps");
        Expect(tie.LodTopologies[0] is
            { LogicalVertexCount: 8, TriangleCount: 4, StripCount: 2, UnresolvedLogicalVertexCount: 0 }
            && CountNonDegenerateTriangles(tie.LodTopologies[0]) == 2
            && tie.LodTopologies.Skip(1).All(lod => lod is
                { LogicalVertexCount: 0, TriangleCount: 0, StripCount: 0, UnresolvedLogicalVertexCount: 0 }),
            "UYA TIE LOD0 should contain authored geometry while higher LODs remain disabled");
        Expect(tie.PacketTables[0].Packets.Count == 2
            && tie.PacketTables[0].Packets.Select(packet => packet.BfcDistance).SequenceEqual(new byte[] { 0, 127 })
            && tie.PacketTables.Skip(1).All(table => table.Offset == 0 && table.Packets.Count == 0),
            "generated UYA TIE should store packets only in LOD0 and retain material backface culling");
        Expect(tie.Header.PacketTableOffsets[0] == 0x100
            && tie.Header.RgbaRemapOffsets[0] < tie.Header.RgbaRemapOffsets[1]
            && tie.Header.RgbaRemapOffsets[1] == tie.Header.RgbaRemapOffsets[2]
            && (tie.Header.VertexNormalsOffset + tie.Header.RgbaRemapOffsets[0]) % 0x10 == 0
            && tie.Header.AmbientRgbaOffset == 0
            && tie.Header.AmbientSize == 8
            && tie.Header.CacheSizes.SequenceEqual(new short[] { 2, 0, 0 }),
            "generated UYA TIE should retain retail bounds storage, model-sized ambient lighting, and an LOD0-only RGBA cache");
        Expect(tie.PacketTables.SelectMany(table => table.Packets).All(packet =>
                packet.RgbaCount == packet.VertexSize)
            && tie.PacketDataBlocks.All(block =>
                BitConverter.ToInt32(converted.ModelBytes, block.Offset + 0x0c) == -9
                && block.UnpackHeader is
                {
                    Unknown0: 1,
                    Unknown1: 1,
                    Unknown2: 6,
                    Unknown4: 7,
                    Unknown5: 0,
                    Unknown6: 0,
                    Unknown7: 0,
                    FatVerticesSize: 6,
                    Unknown11: 0,
                }),
            "generated UYA TIE packets should retain the retail VIF and vertex metadata envelope");
        Expect(tie.PacketDataBlocks.SelectMany(block => block.DecodedVertices)
                .SelectMany(vertex => new[] { Math.Abs((int)vertex.X), Math.Abs((int)vertex.Y), Math.Abs((int)vertex.Z) })
                .All(value => value <= 32_640),
            "generated UYA TIE positions should stay inside the retail VU coordinate range");
        Expect(tie.VertexNormals.All(normal =>
                normal.X == 0 && normal.Y == 127 && normal.Z == 0 && normal.Packed == 0x00C0),
            "generated UYA TIE normals should use inward lighting-facing vectors");
        Expect(tie.RgbaRemapOperations.Count == 8
            && converted.Primitives.Select(value => value.NativeMaterialIndex).SequenceEqual([0, 1])
            && tie.LodTopologies[0].Strips.Select(value => value.ShaderIndex).SequenceEqual([0, 1]),
            $"normal and primitive/material mappings should survive semantic reread: remaps={tie.RgbaRemapOperations.Count}, "
            + $"bindings={string.Join(',', converted.Primitives.Select(value => value.NativeMaterialIndex))}, "
            + $"strips={string.Join(',', tie.LodTopologies[0].Strips.Select(value => value.ShaderIndex))}");
        Expect(converted.LodPolicy == "lod0-only",
            "conversion should report its explicit initial LOD policy");

        var rectangularImage = asset.Images[0] with
        {
            Width = 4,
            Height = 2,
            Rgba32 = Enumerable.Repeat(asset.Images[0].Rgba32, 2).SelectMany(value => value).ToArray(),
        };
        var squareTexture = PifReader.Read(StaticModelConverter.Convert(
            GameId.UYA, asset with { Images = [rectangularImage] }, 7).Textures.Single().PifBytes);
        Expect(squareTexture.Header is { USize: 4, VSize: 4, MipLevels: 3 }
            && squareTexture.MipPixelData.Select(value => value.Length).SequenceEqual([4, 1]),
            "rectangular source images should use the square texture allocation expected by UYA TIE mip uploads");

        var isolatedTrianglesAsset = asset with
        {
            Primitives = [asset.Primitives[0] with
            {
                Indices = Enumerable.Range(0, 14).SelectMany(_ => new uint[] { 0, 1, 2 }).ToArray(),
            }]
        };
        var isolatedTriangles = StaticModelConverter.Convert(GameId.UYA, isolatedTrianglesAsset, 8);
        var isolatedTrianglesTie = TieClassReader.Read(isolatedTriangles.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(isolatedTriangles.Primitives.Single() is { TriangleCount: 14, PacketCount: 1 }
            && isolatedTrianglesTie.PacketTables[0].Packets.Count == 1
            && isolatedTrianglesTie.PacketTables.Skip(1).All(table => table.Packets.Count == 0)
            && isolatedTrianglesTie.LodTopologies[0] is
                { LogicalVertexCount: 56, TriangleCount: 28, StripCount: 14 }
            && CountNonDegenerateTriangles(isolatedTrianglesTie.LodTopologies[0]) == 14,
            "isolated triangles should use independent even-length strips with degenerate padding");
        Expect(isolatedTrianglesTie.PacketDataBlocks.Single().StripControls
                .All(strip => strip.TokenCount == 4),
            "isolated triangles should use the retail four-token strip shape");
        Expect(isolatedTrianglesTie.PacketDataBlocks.Single().DecodedVertices
                .Select(vertex => (int)vertex.GsPacketWriteOffset)
                .SequenceEqual(Enumerable.Range(0, 14).Reverse().SelectMany(strip =>
                    Enumerable.Range(0, 4).Select(vertex => 7 + strip * 13 + vertex * 3))),
            "multi-strip packet vertex rows should use retail reverse-strip storage order");
        var isolatedRemaps = isolatedTrianglesTie.RgbaRemapOperations.OrderBy(operation => operation.OperationIndex).ToArray();
        Expect(isolatedRemaps.SelectMany(operation => operation.SourceSlots).All(slot => slot == 0)
            && isolatedRemaps.Select(operation => operation.TargetSlot).SequenceEqual(
                Enumerable.Range(0, 14).SelectMany(strip =>
                    Enumerable.Range(0, 4).Select(vertex => (13 - strip) * 4 + vertex))),
            "normal/RGBA remaps should target each logical vertex's reversed packet upload row");

        const int isolatedStressTriangleCount = 1_800;
        var isolatedStress = StaticModelConverter.Convert(GameId.UYA, asset with
        {
            Primitives = [asset.Primitives[0] with
            {
                Indices = Enumerable.Range(0, isolatedStressTriangleCount)
                    .SelectMany(_ => new uint[] { 0, 1, 2 })
                    .ToArray(),
            }],
        }, 8);
        var isolatedStressTie = TieClassReader.Read(isolatedStress.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(isolatedStressTie.PacketTables[0].Packets.Count == 120
            && isolatedStressTie.LodTopologies[0].StripCount == isolatedStressTriangleCount
            && CountNonDegenerateTriangles(isolatedStressTie.LodTopologies[0]) == isolatedStressTriangleCount,
            "large disconnected meshes should retain every isolated triangle without quadratic strip discovery");

        var adjacentTrianglesAsset = asset with
        {
            Primitives = [asset.Primitives[0] with
            {
                Positions = [0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f, 2f, 2f, 0f],
                Normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f],
                TexCoords = [0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f],
                Indices = [0u, 1u, 2u, 2u, 1u, 3u],
            }],
        };
        var adjacentTriangles = StaticModelConverter.Convert(GameId.UYA, adjacentTrianglesAsset, 9);
        var adjacentTrianglesTie = TieClassReader.Read(adjacentTriangles.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        var adjacentTopology = adjacentTrianglesTie.LodTopologies[0];
        Expect(adjacentTriangles.Primitives.Single() is { TriangleCount: 2, PacketCount: 1 }
            && adjacentTopology is
                { LogicalVertexCount: 4, TriangleCount: 2, StripCount: 1 }
            && adjacentTopology.LogicalVertices.Select(vertex => vertex.DecodedVertex!)
                .Select(vertex => ((int)vertex.X, (int)vertex.Y, (int)vertex.Z))
                .SequenceEqual([(0, 0, 0), (0, 0, 32640), (32640, 0, 0), (32640, 0, 32640)])
            && adjacentTopology.Triangles.Select(triangle => (triangle.A, triangle.B, triangle.C))
                .SequenceEqual([(0, 2, 1), (1, 2, 3)])
            && CountNonDegenerateTriangles(adjacentTopology) == 2
            && adjacentTrianglesTie.RgbaRemapOperations.Count == 4
            && adjacentTrianglesTie.LodTopologies.Skip(1).All(lod => lod.LogicalVertexCount == 0),
            "adjacent authored triangles should share one four-token retail strip");

        var unindexedAdjacent = adjacentTrianglesAsset with
        {
            Primitives = [adjacentTrianglesAsset.Primitives[0] with
            {
                Positions = [0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f, 0f, 2f, 0f, 2f, 0f, 0f, 2f, 2f, 0f],
                Normals = Enumerable.Repeat(new[] { 0f, 0f, 1f }, 6).SelectMany(value => value).ToArray(),
                TexCoords = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f, 1f],
                Indices = [0u, 1u, 2u, 3u, 4u, 5u],
            }],
        };
        var unindexedAdjacentTie = TieClassReader.Read(
            StaticModelConverter.Convert(GameId.UYA, unindexedAdjacent, 9).ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(unindexedAdjacentTie.LodTopologies[0] is
                { LogicalVertexCount: 4, TriangleCount: 2, StripCount: 1 }
            && CountNonDegenerateTriangles(unindexedAdjacentTie.LodTopologies[0]) == 2,
            "identical unindexed vertices should weld into shared-edge strips before packet construction");

        var transformed = asset with
        {
            Primitives = [asset.Primitives[0] with
            {
                Positions = [10f, 20f, 30f, 12f, 20f, 30f, 10f, 24f, 30f],
                Indices = [0u, 1u, 2u],
            }],
            Bounds = new(10f, 20f, 30f, 12f, 24f, 30f),
        };
        var transformedTie = TieClassReader.Read(
            StaticModelConverter.Convert(GameId.UYA, transformed, 7).ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(transformedTie.Header.BoundingSphere.Radius > 0
            && transformedTie.LodTopologies[0].LogicalVertices.Count == 4
            && transformedTie.Header.PacketTableOffsets[0] == 0x100,
            "pre-baked node transforms should produce valid quantized geometry and bounds");

        var repeatUvAsset = asset with
        {
            Primitives = [asset.Primitives[1] with
            {
                TexCoords = [-0.5f, 0f, 0.5f, 0f, 1f, 1f],
            }],
        };
        var repeatUvTie = TieClassReader.Read(
            StaticModelConverter.Convert(GameId.UYA, repeatUvAsset, 11).ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Expect(repeatUvTie.LodTopologies[0].LogicalVertices.Select(value => value.DecodedVertex!.S)
                .SequenceEqual(new ushort[] { 2048, 8192, 6144, 6144 }),
            "repeat-wrapped negative UVs should shift by whole texture periods without changing their spacing");

        Reject(asset with { Primitives = [asset.Primitives[0] with { Indices = [0u, 1u, 99u] }] },
            "mesh 2, primitive 4");
        Reject(asset with { Primitives = [asset.Primitives[0] with { TexCoords = [-1f, 0f, 1f, 0f, 0f, 1f] }] },
            "fixed-point range");
        Reject(asset with { Primitives = [asset.Primitives[0] with
            { Colors = [1f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f] }] },
            "vertex colors");
        Reject(asset with { Bounds = new(0, 0, 0, 1, 1, 0) }, "canonical bounds");
        Reject(TooManyMaterialsAsset(), "at most 16 referenced materials");
        var denseAsset = DenseStripAsset();
        var dense = StaticModelConverter.Convert(GameId.UYA, denseAsset, 10);
        var denseTie = TieClassReader.Read(dense.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        var denseRemapGroupCount = denseTie.RgbaRemapOperations
            .Select(value => value.GroupIndex).Distinct().Count();
        var densePacketCacheBoundaries = new HashSet<int> { 0 };
        var denseCacheSlotCount = 0;
        foreach (var packet in denseTie.PacketTables[0].Packets)
        {
            denseCacheSlotCount += (packet.RgbaCount + 3) & ~3;
            densePacketCacheBoundaries.Add(denseCacheSlotCount);
        }
        var denseRemapHeaderOffset = checked((int)denseTie.Header.VertexNormalsOffset
            + denseTie.Header.RgbaRemapOffsets[0]);
        Expect(dense.Primitives.Single() is { TriangleCount: 1818, PacketCount: > 1 and <= 255 }
            && denseRemapGroupCount <= 32
            && denseTie.PacketTables[0].Packets.Count > denseRemapGroupCount
            && denseTie.RgbaRemapOperations.Select(value => value.GroupTargetSlotBase).Distinct()
                .All(densePacketCacheBoundaries.Contains)
            && Enumerable.Range(0, denseRemapGroupCount)
                .All(index => dense.ModelBytes[denseRemapHeaderOffset + index] is > 0 and <= 0x40)
            && RemapGroupsCoverPaddedCache(dense.ModelBytes, denseTie, denseRemapGroupCount)
            && denseTie.VertexNormals.Count <= 512
            && denseTie.PacketDataBlocks.SelectMany(block => block.StripControls)
                .All(strip => strip.TokenCount == 4)
            && denseTie.PacketDataBlocks.All(block => block.StripControls.All(strip =>
                strip.VuAddress + 1 + strip.TokenCount * 3 <= 0xcc))
            && CountNonDegenerateTriangles(denseTie.LodTopologies[0]) == 1818,
            "higher-poly geometry should use four-token strips and keep packet-aligned remap groups and VU uploads inside retail boundaries");

        var remapPressure = StaticModelConverter.Convert(GameId.UYA, RemapPackingPressureAsset(), 13);
        var remapPressureTie = TieClassReader.Read(remapPressure.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        var remapPressureGroupCount = remapPressureTie.RgbaRemapOperations
            .Select(value => value.GroupIndex).Distinct().Count();
        var remapPressurePacketBoundaries = new HashSet<int> { 0 };
        var remapPressureCacheSlots = 0;
        foreach (var packet in remapPressureTie.PacketTables[0].Packets)
        {
            remapPressureCacheSlots += (packet.RgbaCount + 3) & ~3;
            remapPressurePacketBoundaries.Add(remapPressureCacheSlots);
        }
        Expect(remapPressure.Primitives.Single() is { TriangleCount: 4000, PacketCount: 100 }
            && remapPressureTie.LodTopologies[0].LogicalVertexCount == 6000
            && remapPressureGroupCount == 25
            && remapPressureTie.RgbaRemapOperations.Select(value => value.GroupTargetSlotBase).Distinct()
                .All(remapPressurePacketBoundaries.Contains)
            && remapPressureTie.PacketDataBlocks.SelectMany(block => block.StripControls)
                .All(strip => strip.TokenCount == 6)
            && CountNonDegenerateTriangles(remapPressureTie.LodTopologies[0]) == 4000,
            "near-capacity geometry should use compact six-token strips and retain packet-aligned remaps");

        var denseNormals = StaticModelConverter.Convert(GameId.UYA, DenseNormalPaletteAsset(), 12);
        var denseNormalsTie = TieClassReader.Read(denseNormals.ModelBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        var expectedAmbientSize = ((denseNormalsTie.VertexNormals.Count + 2) * sizeof(ushort) + 3) & ~3;
        Expect(denseNormalsTie.VertexNormals.Count == 512
            && denseNormalsTie.VertexNormals.Count < denseNormalsTie.LodTopologies[0].LogicalVertexCount
            && denseNormalsTie.Header.AmbientSize == expectedAmbientSize
            && denseNormalsTie.RgbaRemapOperations.SelectMany(value => value.SourceSlots)
                .All(value => value < denseNormalsTie.VertexNormals.Count),
            "large authored normal sets should quantize into the retail 512-entry scratchpad palette");

        Reject(TooManySourceTrianglesAsset(), "source triangles before packet construction");
        Reject(TooManyPacketsAsset(), "at most 127 packets");
        ExpectThrows<NotSupportedException>(() => StaticModelConverter.Convert(GameId.DL, asset, 7),
            "unsupported target game");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(
            () => StaticModelConverter.Convert(GameId.UYA, asset, 7, cancellation.Token),
            "cancelled conversion");

        Console.WriteLine($"UYA static TIE composer tests passed ({converted.ModelSha256}).");
    }

    private static StaticModelCanonicalAsset SharedTextureAsset()
    {
        var positions = new[] { 0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f };
        var normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f };
        var texCoords = new[] { 0f, 0f, 1f, 0f, 0f, 1f };
        return new(
            StaticModelCanonicalFormat.Version,
            [
                new(2, 4, "first", 0, positions, normals, texCoords, null, [0u, 1u, 2u]),
                new(3, 1, "second", 1, positions, null, texCoords, null, [0u, 1u, 2u]),
            ],
            [new(9729, 9729, 33071, 10497), new(9729, 9729, 10497, 33071)],
            [new("shared-a", 0, 0), new("shared-b", 0, 1)],
            [
                new("clamp-u", [1f, 1f, 1f, 1f], 0, 1f, 1f, "OPAQUE", 0.5f, true),
                new("clamp-v", [1f, 1f, 1f, 1f], 1, 1f, 1f, "OPAQUE", 0.5f, false),
            ],
            [new("checker", 2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255])],
            new(0, 0, 0, 2, 2, 0));
    }

    private static StaticModelCanonicalAsset TooManyMaterialsAsset()
    {
        var source = SharedTextureAsset();
        var materials = Enumerable.Range(0, 17)
            .Select(index => source.Materials[0] with { Name = $"material-{index}" })
            .ToArray();
        var primitives = Enumerable.Range(0, 17)
            .Select(index => source.Primitives[0] with
            {
                SourcePrimitiveIndex = index,
                MaterialIndex = index,
            })
            .ToArray();
        return source with { Materials = materials, Primitives = primitives };
    }

    private static StaticModelCanonicalAsset TooManySourceTrianglesAsset()
    {
        var source = SharedTextureAsset();
        var indices = Enumerable.Range(0, 7_651)
            .SelectMany(_ => new uint[] { 0, 1, 2 })
            .ToArray();
        return source with { Primitives = [source.Primitives[0] with { Indices = indices }] };
    }

    private static StaticModelCanonicalAsset TooManyPacketsAsset()
    {
        var source = SharedTextureAsset();
        var primitives = Enumerable.Range(0, StaticModelNativeLimits.MaximumPackets + 1)
            .Select(index => source.Primitives[0] with { SourcePrimitiveIndex = index })
            .ToArray();
        return source with { Primitives = primitives };
    }

    private static StaticModelCanonicalAsset DenseStripAsset()
    {
        const int triangleCount = 1818;
        var source = SharedTextureAsset();
        var positions = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(index => new[] { (float)index, (float)(index % 2), 0f })
            .ToArray();
        var normals = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(_ => new[] { 0f, 0f, 1f })
            .ToArray();
        var texCoords = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(index => new[] { index / (float)(triangleCount + 1), (float)(index % 2) })
            .ToArray();
        var indices = Enumerable.Range(0, triangleCount)
            .SelectMany(index => index % 2 == 0
                ? new uint[] { (uint)index, (uint)(index + 2), (uint)(index + 1) }
                : new uint[] { (uint)index, (uint)(index + 1), (uint)(index + 2) })
            .ToArray();
        return source with
        {
            Primitives = [source.Primitives[0] with { Positions = positions, Normals = normals, TexCoords = texCoords, Indices = indices }],
            Bounds = new(0, 0, 0, triangleCount + 1, 1, 0),
        };
    }

    private static StaticModelCanonicalAsset DenseNormalPaletteAsset()
    {
        var source = DenseStripAsset();
        var primitive = source.Primitives.Single();
        var vertexCount = primitive.Positions.Length / 3;
        const float goldenAngle = 2.39996323f;
        var normals = Enumerable.Range(0, vertexCount).SelectMany(index =>
        {
            var y = 0.5f * (index + 0.5f) / vertexCount;
            var radius = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
            var angle = index * goldenAngle;
            return new[] { MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius };
        }).ToArray();
        return source with { Primitives = [primitive with { Normals = normals }] };
    }

    private static StaticModelCanonicalAsset RemapPackingPressureAsset()
    {
        const int triangleCount = 4000;
        var source = SharedTextureAsset();
        var positions = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(index => new[] { (float)index, (float)(index % 2), 0f })
            .ToArray();
        var normals = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(_ => new[] { 0f, 0f, 1f })
            .ToArray();
        var texCoords = Enumerable.Range(0, triangleCount + 2)
            .SelectMany(index => new[] { index / (float)(triangleCount + 1), (float)(index % 2) })
            .ToArray();
        var indices = Enumerable.Range(0, triangleCount)
            .SelectMany(index => index % 2 == 0
                ? new uint[] { (uint)index, (uint)(index + 2), (uint)(index + 1) }
                : new uint[] { (uint)index, (uint)(index + 1), (uint)(index + 2) })
            .ToArray();
        return source with
        {
            Primitives = [source.Primitives[0] with
            {
                Positions = positions,
                Normals = normals,
                TexCoords = texCoords,
                Indices = indices,
            }],
            Bounds = new(0, 0, 0, triangleCount + 1, 1, 0),
        };
    }

    private static int CountNonDegenerateTriangles(TieLodTopology topology)
    {
        return topology.Triangles.Count(triangle =>
        {
            var a = Position(triangle.A);
            var b = Position(triangle.B);
            var c = Position(triangle.C);
            return a != b && b != c && a != c;
        });

        (short X, short Y, short Z) Position(int index)
        {
            var vertex = topology.LogicalVertices[index].DecodedVertex
                ?? throw new InvalidOperationException("Expected decoded TIE vertex.");
            return (vertex.X, vertex.Y, vertex.Z);
        }
    }

    private static bool RemapGroupsCoverPaddedCache(byte[] bytes, TieClass tie, int groupCount)
    {
        var chunkOffset = checked((int)tie.Header.VertexNormalsOffset + tie.Header.RgbaRemapOffsets[0]);
        var groupOffset = chunkOffset + 0x20;
        var outputQwordCount = 0;
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupByteLength = bytes[chunkOffset + groupIndex] * 0x10;
            var directBytes = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset));
            var zeroBytes = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0a));
            var groupOutputQwords = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0c));
            if (directBytes / 4 + zeroBytes / 2 != groupOutputQwords * 4)
                return false;
            outputQwordCount += groupOutputQwords;
            groupOffset += groupByteLength;
        }
        return outputQwordCount == tie.Header.CacheSizes[0];
    }

    private static void Reject(StaticModelCanonicalAsset asset, string expectedMessage)
    {
        try
        {
            _ = StaticModelConverter.Convert(GameId.UYA, asset, 7);
        }
        catch (InvalidDataException exception) when (exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected UYA static TIE rejection containing '{expectedMessage}'.");
    }

    private static void ExpectThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
