using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;

internal static class GcTieSplitConversionTests
{
    public static void Run()
    {
        var legacy = Convert.FromBase64String("AAEAABABAAAQAQAAAQAAAQAAAAAAAAAAAAAAAIACAAAAAAAAAwAAAAAAEAAAAAAAAAAAAPABAAABAAgAAAAAAAAAgD9pAAAAAAAAABAgQIAAAAAAAAAAAAAAAAAAAAAADAAGAAMAAAAAAAAAAAAAAAAAAAAAAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAABfwYCBAkJAA0BAAAAAAAAAAAAAAAAAAD3////AAAAAAAAAAAAAAAAAAAAAAEBBgMHAAAAFgYJAAQABgAEABMABAAgAAAAAAAAAAAAAAAAAAAABwAAAAAAABAAAOgDAAAAAAoAZADIAAAQFAAAAAAA6AMNAMgAkAEAEBAAAAAAAOgDFwDIAJABABAAAAAAAAD6ABoALAFYAgAQHQDoAwAAAAAhAJABIAMAEAAAAAAAAOgDJAD0AegDABAAANAHAADQBycAWAKwBAAQAADQBwAA0AcqALwCeAUAEAAABwMDA/wDAwP8AwMD9gAAAAB/AAAAf8AAAAAAAAAAAAAFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAABAAIAAgACAAGAAMAUAAAAAAAAAAYAPwHHAAAACAABAAAAAgAAAAMAAAAAAAAAAAAEAAAAAAAAAAUACQAKAAsAAAAAAAAAAAAAAAAAAAAAgAEABwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var legacyTie = Read(legacy);
        try
        {
            GcTieVuUploadAssertions.Validate(legacyTie.PacketDataBlocks[0]);
            throw new Exception("Expected the original corrupt split packet to fail VU validation.");
        }
        catch (Exception exception) when (exception.Message.Contains("VU loop")) { }
        var repairedLegacy = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, Fixture(false), legacy, 0x69, 0x69);
        Expect(repairedLegacy is not null, "earlier split packets are recognized for project repair");
        GcTieConversionAssertions.Equivalent(Fixture(false), repairedLegacy!);
        legacy[0x4c] ^= 1;
        Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, Fixture(false), legacy, 0x69, 0x69) is null,
            "earlier split migration preserves independent edits");
        var intermediateFat = Convert.FromBase64String("AAEAABABAAAQAQAAAQAAAQAAAAAAAAAAAAAAAJACAAAAAAAAAwAAAAAAEAAAAAAAAAAAAAACAAABAAgAAAAAAAAAgD9pAAAAAAAAABAgQIAAAAAAAAAAAAAAAAAAAAAADAAGAAMAAAAAAAAAAAAAAAAAAAAAAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAABfwYCBAoJAA4BAAAAAAAAAAAAAAAAAAD3////AAAAAAAAAAAAAAAAAAAAAAABBgMXCioAFAkIAQQABgAEABMABAAgAAAAAAAAAAAAAAAAAAAABwAAAAAAABAAAAAAAADoAxcAyACQAQAQAADoAwAAAAAhAJABIAMAEAAAAAAAAOgDJAD0AegDABAAANAHAADQBycAWAKwBAAQAADoAwAAAAAKAGQAyAAAEBQAAAAAAOgDDQDIAJABABAQAAAAAAD6ABoALAFYAgAQHQAAAAAAAAAqANAHAADQBwAAvAJ4BQAQAAAAAAAAAAAAAAcDAwP8AwMD/AMDA/YAAAAAfwAAAH/AAAAAAAAAAAAABQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAQACAAIAAgABgADAFAAAAAAAAAADAD8BxAAAAAgABQAAAAEAAAAGAAAAAAAAAAAABwAAAAAAAAACAAkACgALAAAAAAAAAAAAAAAAAAAAAIAEAAUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
        var repairedFat = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, SmallFatFixture(1), intermediateFat, 0x69, 0x69);
        Expect(repairedFat is not null, "intermediate upload-group conversions are recognized for project repair");
        GcTieConversionAssertions.Equivalent(SmallFatFixture(1), repairedFat!);
        foreach (var multipass in new[] { false, true })
        {
            var source = Fixture(multipass);
            var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0x69);
            GcTieConversionAssertions.Equivalent(source, converted);
            var target = Read(converted);
            Expect(target.LodTopologies[0].StripCount == 3 && target.LodTopologies[0].LogicalVertexCount == 12,
                "folded quad splits into two culled strips with even padding");
            Expect(target.RgbaRemapOperations.Select(value => value.Kind).Distinct().Count() == 5,
                "all five lighting recipe operations survive splitting");
            Expect(target.PacketDataBlocks[0].DecodedVertices.Any(vertex => vertex.SecondaryGsPacketWriteOffset != 0) == !multipass,
                "base-pass duplicate vertices retain shared GS writes when the groups fit the VU pipeline");
            var glow = (int)target.Header.VertexNormalsOffset + target.Header.UnknownOffsets78[0];
            Expect(BinaryPrimitives.ReadUInt16LittleEndian(converted.AsSpan(glow)) == (multipass ? 3 : 2),
                "glow-cache targets follow duplicated or shared source vertices");
            var previous = source.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(previous.AsSpan(0x44), 0x9009);
            var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x9009, 0x69);
            Expect(repaired is not null && repaired.AsSpan().SequenceEqual(converted),
                "previously copied mixed-facing models can be repaired with their source class");
            previous[0x4c] ^= 1;
            Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x9009, 0x69) is null,
                "repair preserves independently edited mixed-facing models");
            Expect(LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0x69)
                .AsSpan().SequenceEqual(converted), "split conversion is deterministic");
        }
        foreach (var fatCount in new[] { 1, 4 })
        {
            var source = SmallFatFixture(fatCount);
            var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0x69);
            GcTieConversionAssertions.Equivalent(source, converted);
            var target = Read(converted);
            Expect(target.PacketDataBlocks.SelectMany(block => block.DecodedVertices).Any(vertex =>
                vertex.Kind == TiePacketDecodedVertexKind.Fat
                && vertex.SecondaryGsPacketWriteOffset == vertex.GsPacketWriteOffset),
                "single-write fat tails still execute both VU fat phases");
            var fatBlock = target.PacketDataBlocks.First(block => block.UnpackHeader!.Unknown11 > 0);
            converted[fatBlock.Offset + 0x21] = 1;
            try
            {
                GcTieVuUploadAssertions.Validate(Read(converted).PacketDataBlocks[fatBlock.PacketIndex]);
                throw new Exception("Expected a skipped fat phase to fail VU validation.");
            }
            catch (Exception exception) when (exception.Message.Contains("fat loop")) { }
        }
        var coincident = Fixture(false);
        var coincidentPoints = new[] { (0, 0), (1000, 0), (0, 1000), (1000, 1000) };
        for (var index = 0; index < 8; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(coincident.AsSpan(0x150 + index * 16),
                (short)coincidentPoints[index % 4].Item1);
            BinaryPrimitives.WriteInt16LittleEndian(coincident.AsSpan(0x154 + index * 16),
                (short)coincidentPoints[index % 4].Item2);
        }
        coincident[0x134] = 20;
        var previousFacing = Convert.FromBase64String("AAEAAAAAAAAAAAAAAQAAAQAAAAAAAAAAAAAAAGACAAAAAAAAAgAAAAAAEAAAAAAAAAAAAOABAAABAAgAAAAAAAAAgD9pAAAAAAAAABAgQIAAAAAAAAAAAAAAAAAAAAAACAAEAAIAAAAAAAAAAAAAAAAAAAAAAAAAcAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAABfwUCBAgIAAwBAAAAAAAAAAAAAAAAAAD3////AAAAAAAAAAAAAAAAAAAAAAEBBgIUAAAAFAYIAAQABgAEABMgAAAAAAAAAAAAAAAAAAAAAAAABwAAAAAAABAAAOgDAAAAAAoAZADIAAAQAAAAAAAA6AMNAMgAkAEAEAAA6AMAAOgDEAAsAVgCABAAAAAAAAAAABQAkAEgAwAQAADoAwAAAAAXAPQB6AMAEAAAAAAAAOgDGgBYArAEABAAAOgDAADoAx0AvAJ4BQAQAAAHAwMD/AMDA/YAAAAAAAAAAH8AAAB/wAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAEAAQACAAIAAAAAgBAAAAAAAAAABQA/AcYAAAAHAAEAAAACAAAAAAAAAAAAAwAAAAAAAAAEAAAAAAAAAAAAAIABAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
        var correctedFacing = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, coincident, 0x69);
        GcTieConversionAssertions.Equivalent(coincident, correctedFacing);
        Expect(previousFacing.Length == correctedFacing.Length,
            "coincident facing repair retains the established packet layout");
        var differences = previousFacing.Zip(correctedFacing).Where(pair => pair.First != pair.Second).ToArray();
        Expect(differences.Length > 0 && differences.All(pair => (pair.First ^ pair.Second) == 0x20),
            "coincident facing repair changes only strip facing flags");
        var coincidentTie = Read(correctedFacing);
        var coincidentTopology = coincidentTie.LodTopologies[0];
        var normals = coincidentTopology.Triangles.Select(triangle =>
        {
            System.Numerics.Vector3 Position(int index)
            {
                var vertex = coincidentTopology.LogicalVertices[index].DecodedVertex!;
                return new(vertex.X, vertex.Y, vertex.Z);
            }
            return System.Numerics.Vector3.Cross(Position(triangle.B) - Position(triangle.A),
                Position(triangle.C) - Position(triangle.A));
        }).Where(normal => normal.LengthSquared() > 0).ToArray();
        Expect(normals.Length == 4 && normals.All(normal => System.Numerics.Vector3.Dot(normal, normals[0]) > 0),
            "overlapping flat faces retain the same facing instead of opposing boundary adjacency");
        var repairedFacing = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, coincident, previousFacing, 0x69, 0x69);
        Expect(repairedFacing is not null && repairedFacing.AsSpan().SequenceEqual(correctedFacing),
            "previous facing conversions repair automatically");
        previousFacing[0x4c] ^= 1;
        Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, coincident, previousFacing, 0x69, 0x69) is null,
            "coincident facing migration preserves independent edits");
        var fatSource = FatSplitFixture();
        var fatConverted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, fatSource, 0x69);
        GcTieConversionAssertions.Equivalent(fatSource, fatConverted);
        Expect(Read(fatConverted).PacketDataBlocks.Any(block =>
            block.DecodedVertices.Count(vertex => vertex.Kind == TiePacketDecodedVertexKind.Dinky) == 4
            && block.DecodedVertices.Where(vertex => vertex.Kind == TiePacketDecodedVertexKind.Dinky)
                .Select(vertex => vertex.GsPacketWriteOffset).Distinct().Count() == 1),
            "fat-only split batches preload four harmless dinky writes");
        foreach (var count in new[] { 128, 255 })
        {
            var source = RepeatedFixture(count);
            var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0x69);
            Expect(Read(converted).PacketTables[0].Count == count, "packet counts use all eight unsigned bits");
            GcTieConversionAssertions.Equivalent(source, converted);
        }
        Console.WriteLine("GC folded strips, weighted lighting, glow, UV passes and unsigned packet counts passed.");
    }

    public static void Qualify(string directory)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.bin").Order(StringComparer.Ordinal))
        {
            var source = File.ReadAllBytes(file);
            var tie = Read(source);
            var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie,
                source, (ushort)tie.Header.OClass);
            try { GcTieConversionAssertions.Equivalent(source, converted); }
            catch (Exception exception) { throw new Exception($"{Path.GetFileName(file)}: {exception.Message}", exception); }
            var result = Read(converted);
            Console.WriteLine($"0x{(ushort)tie.Header.OClass:X4}: packets {string.Join('/', tie.Header.PacketCounts)}"
                + $" -> {string.Join('/', result.Header.PacketCounts)}, bytes {source.Length} -> {converted.Length}");
            count++;
        }
        Expect(count > 0, "retail qualification directory contains TIE models");
        Console.WriteLine($"Qualified exact geometry/material/lighting/glow/UV preservation and VU limits for {count} retail GC models.");
    }

    private static byte[] Fixture(bool multipass)
    {
        const int packet = 0x110;
        var packetQwords = multipass ? 18 : 13;
        var normal = packet + packetQwords * 16;
        var remap = normal + 16;
        var glow = remap + 96;
        var shader = glow + 16;
        var bytes = new byte[shader + 80];
        I32(0, 0x100); bytes[12] = 1; bytes[15] = 1;
        I32(0x1c, shader); I16(0x24, 2); I16(0x2a, remap - normal);
        I32(0x34, normal); I16(0x38, 1); I16(0x3a, 8);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x40), 1);
        I16(0x44, 0x69); I32(0x4c, unchecked((int)0x80402010));
        I16(0x60, 8); I16(0x62, 4); I16(0x64, 2); I16(0x78, glow - normal);
        I32(0x100, 16); bytes[0x104] = 1; bytes[0x105] = 127;
        bytes[0x106] = 5; bytes[0x107] = 2; bytes[0x108] = 4;
        bytes[0x109] = 8; bytes[0x10a] = 8; bytes[0x10c] = 12; bytes[0x10d] = 1;
        bytes[packet + 0x20] = 1; bytes[packet + 0x21] = 1; bytes[packet + 0x22] = 6;
        bytes[packet + 0x23] = 2; bytes[packet + 0x24] = 7;
        bytes[packet + 0x28] = 20; bytes[packet + 0x29] = 6; bytes[packet + 0x2a] = 8;
        bytes[packet + 0x2c] = 4; bytes[packet + 0x2e] = 6;
        bytes[packet + 0x30] = 4; bytes[packet + 0x32] = 19;
        var points = new[] { (0, 0), (1000, 0), (0, 1000), (0, 250),
            (1000, 0), (0, 1000), (2000, 2000), (2000, 2000) };
        for (var index = 0; index < points.Length; index++)
        {
            var offset = packet + 64 + index * 16;
            I16(offset, points[index].Item1); I16(offset + 4, points[index].Item2);
            I16(offset + 6, 7 + index * 3 + (index >= 4 ? 1 : 0));
            I16(offset + 8, index * 100); I16(offset + 10, index * 200); I16(offset + 12, 4096);
            bytes[packet + 192 + index] = index == 0 ? (byte)7 : index == 4 ? (byte)0xfc : (byte)3;
        }
        bytes[packet + 200] = 0xf6;
        if (multipass)
        {
            bytes[0x10b] = 13; bytes[0x10e] = 1; bytes[0x10f] = 2;
            I32(packet + 13 * 16, 80); I32(packet + 13 * 16 + 32, 8);
            for (var index = 0; index < 8; index++) I32(packet + 16 * 16 + index * 4, 0x32100000 + index);
        }
        bytes[normal + 1] = 127; bytes[normal + 5] = 127; I16(normal + 6, 0xc0);
        bytes[remap] = 4;
        var group = remap + 32;
        I16(group, 16); I16(group + 2, 4); I16(group + 4, 4); I16(group + 6, 8); I16(group + 8, 8);
        I16(group + 12, 2); I16(group + 14, 64);
        var cursor = group + 16;
        foreach (var index in new[] { 0, 5, 6, 7 })
        {
            I16(cursor, index == 6 ? 0x7fc : 0); I16(cursor + 2, index * 4); cursor += 4;
        }
        I32(cursor, 4); cursor += 4;
        I32(cursor, 8); cursor += 4;
        I16(cursor + 6, 12); cursor += 8;
        I16(cursor + 6, 16);
        I16(glow, 2); I16(glow + 2, 4); I16(glow + 4, 24);
        return bytes;

        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void I16(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    }

    private static byte[] SmallFatFixture(int fatCount)
    {
        const int packet = 0x110, vertices = packet + 64, oldScissor = packet + 192;
        var source = Fixture(false);
        var dinky = 8 - fatCount;
        var vertexSize = (128 + fatCount * 8 + 15) / 16;
        var extra = vertexSize * 16 - 128;
        var bytes = new byte[source.Length + extra];
        source.AsSpan(0, vertices).CopyTo(bytes);
        source.AsSpan(oldScissor).CopyTo(bytes.AsSpan(oldScissor + extra));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1c), BitConverter.ToInt32(source, 0x1c) + extra);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x34), BitConverter.ToInt32(source, 0x34) + extra);
        bytes[0x109] = checked((byte)vertexSize); bytes[0x10c] = checked((byte)(4 + vertexSize));
        bytes[packet + 0x28] = checked((byte)(4 + 2 * dinky));
        bytes[packet + 0x29] = checked((byte)(6 + 3 * fatCount));
        bytes[packet + 0x2a] = checked((byte)dinky); bytes[packet + 0x2b] = checked((byte)fatCount);
        var cursor = vertices;
        for (var index = 0; index < 8; index++)
        {
            var vertex = source.AsSpan(vertices + index * 16, 16);
            if (index < dinky) { vertex.CopyTo(bytes.AsSpan(cursor)); cursor += 16; }
            else
            {
                vertex[..6].CopyTo(bytes.AsSpan(cursor + 8));
                vertex.Slice(6, 2).CopyTo(bytes.AsSpan(cursor + 6));
                vertex[8..].CopyTo(bytes.AsSpan(cursor + 16));
                cursor += 24;
            }
        }
        return bytes;
    }

    private static byte[] FatSplitFixture()
    {
        const int packet = 0x110, vertex = packet + 64, scissor = packet + 98 * 16;
        const int normal = packet + 103 * 16, remap = normal + 16, shader = remap + 32 + 272;
        var bytes = new byte[shader + 80];
        var template = Fixture(false);
        template.AsSpan(0, 0x110).CopyTo(bytes);
        I32(0x1c, shader); I32(0x34, normal); I16(0x24, 16); I16(0x2a, 16); I16(0x78, 0);
        I16(0x60, 64); I16(0x62, 60); I16(0x64, 2);
        bytes[0x109] = 94; bytes[0x10a] = 64; bytes[0x10c] = 98; bytes[0x10d] = 5;
        bytes[packet + 0x20] = 1; bytes[packet + 0x21] = 1; bytes[packet + 0x22] = 6;
        bytes[packet + 0x23] = 2; bytes[packet + 0x24] = 7; bytes[packet + 0x26] = 197;
        bytes[packet + 0x28] = 12; bytes[packet + 0x29] = 186;
        bytes[packet + 0x2a] = 4; bytes[packet + 0x2b] = 60;
        bytes[packet + 0x2c] = 4; bytes[packet + 0x2e] = 6;
        bytes[packet + 0x30] = 60; bytes[packet + 0x32] = 19;
        var points = new[] { (0, 0), (1000, 0), (0, 1000), (0, 250) };
        for (var index = 0; index < 64; index++)
        {
            var offset = index < 4 ? vertex + index * 16 : vertex + 64 + (index - 4) * 24;
            var position = offset + (index < 4 ? 0 : 8);
            var uv = offset + (index < 4 ? 8 : 16);
            I16(position, points[index % 4].Item1); I16(position + 4, points[index % 4].Item2);
            I16(offset + 6, 7 + index * 3 + (index >= 4 ? 1 : 0));
            I16(uv, index * 100); I16(uv + 2, index * 200); I16(uv + 4, 4096);
            bytes[scissor + index] = index == 0 ? (byte)7 : index == 4 ? (byte)0xfc : (byte)3;
        }
        bytes[scissor + 64] = 0xf6;
        template.AsSpan((int)Read(template).Header.VertexNormalsOffset, 8).CopyTo(bytes.AsSpan(normal));
        bytes[remap] = 17; I16(remap + 32, 256); I16(remap + 44, 16); I16(remap + 46, 272);
        for (var index = 0; index < 64; index++) I16(remap + 50 + index * 4, index * 4);
        return bytes;
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void I16(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    }

    private static byte[] RepeatedFixture(int count)
    {
        var template = Fixture(false);
        var original = Read(template);
        var packet = original.PacketDataBlocks.Single();
        var data = 0x100 + count * 16;
        var normal = data + packet.Length;
        var remap = normal + 16;
        var groups = (count + 15) / 16;
        var remapSize = 32 + Enumerable.Range(0, groups).Sum(index => 16 + Math.Min(16, count - index * 16) * 8 * 4);
        var shader = remap + remapSize;
        var bytes = new byte[shader + 80];
        template.AsSpan(0, 0x100).CopyTo(bytes);
        bytes[12] = checked((byte)count);
        I32(0x1c, shader); I32(0x34, normal); I16(0x24, count * 2);
        I16(0x2a, 16); I16(0x78, 0);
        I16(0x60, count * 8); I16(0x62, count * 4); I16(0x64, count * 2);
        for (var index = 0; index < count; index++)
        {
            template.AsSpan(0x100, 16).CopyTo(bytes.AsSpan(0x100 + index * 16));
            I32(0x100 + index * 16, data - 0x100);
        }
        packet.Bytes.CopyTo(bytes, data);
        template.AsSpan((int)original.Header.VertexNormalsOffset, 8).CopyTo(bytes.AsSpan(normal));
        var cursor = remap + 32;
        for (var group = 0; group < groups; group++)
        {
            var slots = Math.Min(16, count - group * 16) * 8;
            var length = 16 + slots * 4;
            bytes[remap + group] = checked((byte)(length / 16));
            I16(cursor, slots * 4); I16(cursor + 12, slots / 4); I16(cursor + 14, length);
            for (var slot = 0; slot < slots; slot++) I16(cursor + 16 + slot * 4 + 2, slot * 4);
            cursor += length;
        }
        template.AsSpan((int)original.Header.ShadersOffset, 80).CopyTo(bytes.AsSpan(shader));
        return bytes;

        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void I16(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    }

    private static TieClass Read(byte[] bytes) => TieClassReader.Read(bytes,
        TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.GC)));
    private static void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
}
