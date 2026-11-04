using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;

internal static class GcTieConversionTests
{
    public static void Run()
    {
        var positions = new[] { 0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f };
        var normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f };
        var uv = new[] { 0f, 0f, 1f, 0f, 0f, 1f };
        var asset = new StaticModelCanonicalAsset(StaticModelCanonicalFormat.Version,
            [new(0, 0, "culled", 0, positions, normals, uv, null, [0u, 1u, 2u]),
             new(1, 0, "two-sided", 1, [10f, 0f, 0f, 12f, 0f, 0f, 10f, 2f, 0f], normals, uv, null, [0u, 1u, 2u])],
            [new(9729, 9729, 10497, 10497)], [new("texture", 0, 0)],
            [new("culled", [1f, 1f, 1f, 1f], 0, 1, 1, "OPAQUE", 0.5f, false),
             new("two-sided", [1f, 1f, 1f, 1f], 0, 1, 1, "OPAQUE", 0.5f, true)],
            [new("white", 2, 2, Enumerable.Repeat((byte)255, 16).ToArray())], new(0, 0, 0, 12, 2, 0));
        var native = StaticModelConverter.Convert(GameId.UYA, asset, 0xc6).ModelBytes;
        var source = native.ToArray();
        var tie = Read(native, GameId.UYA);
        foreach (var block in tie.PacketDataBlocks)
        {
            source.AsSpan(tie.PacketTables[block.LodIndex].Packets[block.PacketIndex].AbsoluteDataOffset + 0x0c, 4).Clear();
            foreach (var control in block.StripControls) source[control.Offset + 3] &= 0xdf;
        }
        var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0xc6);
        var result = Read(converted, GameId.UYA);
        var expectedBytes = native.ToArray();
        var changed = 0;
        foreach (var block in tie.PacketDataBlocks)
        {
            var culled = tie.PacketTables[block.LodIndex].Packets[block.PacketIndex].BfcDistance != 0;
            foreach (var control in block.StripControls)
            {
                var expected = culled ? native[control.Offset + 3] : source[control.Offset + 3];
                Expect(converted[control.Offset + 3] == expected, $"GC facing matches native UYA culling: expected {expected}, got {converted[control.Offset + 3]}");
                if (converted[control.Offset + 3] != source[control.Offset + 3]) changed++;
                expectedBytes[control.Offset + 3] = expected;
            }
        }
        Expect(changed > 0, "fixture exercises a winding correction");
        Expect(expectedBytes.AsSpan().SequenceEqual(converted), "only setup words and winding flags change, including preserved bfcDistance");
        Expect(result.Header.OClass == 0xc6, "native class ID is retained");
        Expect(tie.PacketTables.SelectMany(value => value.Packets).Any(value => value.BfcDistance == 0)
            && tie.PacketTables.SelectMany(value => value.Packets).Any(value => value.BfcDistance != 0),
            "fixture covers culled and two-sided packets");
        var packet = tie.PacketTables[0].Packets[0];
        for (byte shaders = 1; shaders <= 4; shaders++)
        {
            var multipleShaders = source.ToArray();
            multipleShaders[checked((int)tie.Header.PacketTableOffsets[0]) + 4] = shaders;
            for (var index = 0; index < 3; index++)
                BinaryPrimitives.WriteInt32LittleEndian(multipleShaders.AsSpan(packet.AbsoluteDataOffset + index * 4),
                    index < shaders - 1 ? 19 + 6 * index : 0);
            var repairedSetup = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA,
                FrontendAssetKind.Tie, multipleShaders, 0xc6);
            Expect(BinaryPrimitives.ReadInt32LittleEndian(repairedSetup.AsSpan(packet.AbsoluteDataOffset + 0x0c))
                == -(4 + 5 * shaders), "packet setup locates strip controls after every uploaded shader");
        }
        var previous = source.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(previous.AsSpan(0x44), 0x9009);
        var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x9009, 0xc6);
        Expect(repaired is not null && repaired.AsSpan().SequenceEqual(converted), "repair restores an old class allocation");
        Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, converted, 0xc6, 0xc6)!.AsSpan().SequenceEqual(converted), "repair is idempotent");
        previous[^1] ^= 1;
        Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x9009, 0xc6) is null, "repair preserves independent model edits");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0xc6, cancellation.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        GcTieSplitConversionTests.Run();
        GcTieOverlapFacingTests.Run();
        GcTieOddStripTests.Run();
        Console.WriteLine("GC tie winding conversion tests passed.");
    }

    private static TieClass Read(byte[] bytes, GameId game) => TieClassReader.Read(bytes,
        TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(game)));
    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
