using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;
using RatchetPs2.Games.RC1.Ties;

internal static class Rc1TieLodLightingTests
{
    public static void Run()
    {
        foreach (var fat in new[] { false, true })
        {
            var source = Fixture(fat);
            var original = source.ToArray();
            var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, 0x1234);
            var tie = TieClassReader.Read(converted);
            Expect(source.SequenceEqual(original), "source lighting and geometry remain unchanged");
            Expect(tie.PacketTables.All(table => table.Packets.Count > 0), "all three geometry LODs survive");
            Verify(tie, 2, 0, 1024, [2]);
            Verify(tie, 1, 0, 1024, [2, 3]);
            Verify(tie, 1, 1024, 1024, [1, 3]);
            Verify(tie, 0, 0, 1024, fat ? [1, 2, 3, 3] : [2, 3]);
            Verify(tie, 0, 1024, 1024, fat ? [1, 1, 2, 3] : [1, 3]);
            foreach (var block in tie.PacketDataBlocks) GcTieVuUploadAssertions.Validate(block);
            var legacySource = source.ToArray();
            var sourceTie = TieClassReader.Read(source, TieClassReadOptions.ForGameProfile(Rc1TieGameProfile.Default));
            foreach (var block in sourceTie.PacketDataBlocks)
                foreach (var vertex in block.DecodedVertices.Where(vertex => vertex.Kind == TiePacketDecodedVertexKind.Fat))
                {
                    var remap = sourceTie.VertexNormalRemaps.Single(value => value.LodIndex == block.LodIndex
                        && value.PacketIndex == block.PacketIndex && value.VertexRowIndex == vertex.SourceRowIndex);
                    legacySource[remap.Offset + 1] = legacySource[remap.Offset + 2] = (byte)remap.NormalIndex;
                }
            // The old converter ignored both endpoint indices and wrote only direct lighting recipes.
            var previous = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, legacySource, 0x1234);
            Expect(!previous.SequenceEqual(converted), "fixture reproduces the earlier lighting conversion");
            var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234);
            Expect(repaired is not null && repaired.SequenceEqual(converted), "earlier direct lighting repairs to native weighted lighting");
            previous[0x4c] ^= 1;
            Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234) is null, "lighting repair preserves independent edits");
        }
        Console.WriteLine("RC1 LOD lighting retains native two/four-color morph endpoints and shared-vertex lighting across all three LODs.");
    }

    internal static byte[] Fixture(bool finestFat)
    {
        var sources = new[] { Rc1AssetConversionTests.Fixture(finestFat), Rc1AssetConversionTests.Fixture(true), Rc1AssetConversionTests.Fixture() };
        using var output = new MemoryStream();
        output.Write(new byte[0x130]);
        sources[0].AsSpan(0, 0x100).CopyTo(output.GetBuffer());
        var tables = new[] { 0x100, 0x110, 0x120 };
        for (var lod = 0; lod < 3; lod++)
        {
            var source = sources[lod];
            var packet = source.AsSpan(0x110, BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(12)) - 0x110).ToArray();
            var remap = source[0x10a] * 16;
            if (lod == 2) new byte[] { 0, 1, 2, 3 }.CopyTo(packet, remap);
            else if (lod == 0 && !finestFat) new byte[] { 0, 1, 4, 5 }.CopyTo(packet, remap);
            else
            {
                packet[remap] = 0; packet[remap + 1] = 1;
                new byte[] { (byte)(lod == 0 ? 6 : 4), (byte)(lod == 0 ? 4 : 2), (byte)(lod == 0 ? 5 : 3), 255,
                    (byte)(lod == 0 ? 7 : 5), 1, (byte)(lod == 0 ? 4 : 3), 255 }.CopyTo(packet, remap + 4);
                I16(packet, 96, lod == 0 ? 513 : 512);
                I16(packet, 120, lod == 0 ? -512 : 0);
                I16(packet, 122, -512);
            }
            var record = source.AsSpan(0x100, 16).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(record, checked((int)output.Position - tables[lod]));
            record.CopyTo(output.GetBuffer(), tables[lod]);
            output.Write(packet);
        }
        var normal = checked((int)output.Position);
        var sourceNormal = BinaryPrimitives.ReadInt32LittleEndian(sources[0].AsSpan(12));
        output.Write(sources[0].AsSpan(sourceNormal, 64 * 8));
        var shaders = checked((int)output.Position);
        output.Write(new byte[80]);
        var bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), normal);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x2c), shaders);
        for (var lod = 0; lod < 3; lod++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(lod * 4), tables[lod]);
            bytes[0x20 + lod] = 1;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x10 + lod * 4), 10 + lod * 10);
        }
        return bytes;
    }

    private static void Verify(TieClass tie, int lod, short x, short y, int[] expected)
    {
        var operations = tie.RgbaRemapOperations.Where(operation => operation.LodIndex == lod)
            .ToDictionary(operation => operation.TargetCacheSlot);
        var colorBase = 0;
        var found = false;
        foreach (var packet in tie.PacketTables[lod].Packets)
        {
            var block = tie.PacketDataBlocks.Single(value => value.LodIndex == lod && value.PacketIndex == packet.PacketIndex);
            foreach (var vertex in block.DecodedVertices.Where(vertex => vertex.X == x && vertex.Y == y))
            {
                var recipe = operations[colorBase + vertex.Index];
                Expect(recipe.SourceSlots.Order().SequenceEqual(expected.Order()), $"LOD{lod} vertex ({x},{y}) weighted lighting");
                // Exercise actual color averaging: directions are lit separately before native remaps combine RGB.
                var colors = new[] { 8, 24, 72, 200, 12, 240, 0, 255 };
                Expect(recipe.SourceSlots.Sum(index => colors[index]) / recipe.SourceSlots.Length
                    == expected.Sum(index => colors[index]) / expected.Length, "native RGB blend matches the RC1 endpoint");
                found = true;
            }
            colorBase += (packet.RgbaCount + 3) & ~3;
        }
        Expect(found, "fixture vertex survives conversion");
    }

    private static void I16(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), checked((short)value));
    private static void Expect(bool value, string message) { if (!value) throw new Exception("RC1 LOD lighting: " + message); }
}
