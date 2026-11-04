using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;

internal static class GcTieOddStripTests
{
    public static void Run()
    {
        var asset = new StaticModelCanonicalAsset(StaticModelCanonicalFormat.Version,
            [new(0, 0, "triangle", 0, [0f, 0f, 0f, 4f, 0f, 0f, 0f, 0f, 4f],
                [0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f], [0f, 0f, 1f, 0f, 0f, 1f], null, [0u, 1u, 2u])],
            [new(9729, 9729, 10497, 10497)], [new("texture", 0, 0)],
            [new("triangle", [1f, 1f, 1f, 1f], 0, 1, 1, "OPAQUE", 0.5f, false)],
            [new("white", 2, 2, Enumerable.Repeat((byte)255, 16).ToArray())], new(0, 0, 0, 4, 0, 4));
        var native = StaticModelConverter.Convert(GameId.UYA, asset, 0x10b).ModelBytes;
        var tie = Read(native);
        var block = tie.PacketDataBlocks.Single();
        var packet = tie.PacketTables[0].Packets.Single();
        var control = block.StripControls.Single();
        var source = native.ToArray();
        source[control.Offset] = 3;
        // Keep the four-upload pipeline but make its padding overwrite the final real vertex.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(packet.AbsoluteDataOffset + packet.VertexOffset * 16 + 3 * 16 + 6), 13);
        source[packet.AbsoluteDataOffset + packet.ScissorOffset * 16 + 3] = 0xf6;
        source.AsSpan(packet.AbsoluteDataOffset + 12, 4).Clear();
        source[control.Offset + 3] &= 0xdf;
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0x60), 3);
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0x62), 1);
        var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0x10b);
        GcTieConversionAssertions.Equivalent(source, converted);
        if (Read(converted).PacketDataBlocks.Any(value => value.StripControls.Any(strip => strip.TokenCount % 2 != 0)))
            throw new Exception("Uniform-facing odd strips must be padded for UYA's paired culling loop.");
        var previous = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(previous.AsSpan(packet.AbsoluteDataOffset + 12), -9);
        previous[control.Offset + 3] = native[control.Offset + 3];
        var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x10b, 0x10b);
        if (repaired is null || !repaired.AsSpan().SequenceEqual(converted))
            throw new Exception("Previously unpadded conversions must repair automatically.");
        previous[0x4c] ^= 1;
        if (LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x10b, 0x10b) is not null)
            throw new Exception("Odd-strip repair must preserve independent model edits.");
        Console.WriteLine("GC uniform odd strips pad safely and previous conversions repair without replacing edits.");
    }

    private static TieClass Read(byte[] bytes) => TieClassReader.Read(bytes,
        TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
}
