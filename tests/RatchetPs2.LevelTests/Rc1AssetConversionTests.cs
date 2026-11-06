using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Core.Ties;
using RatchetPs2.Games.RC1.Ties;
using RatchetPs2.Sdk;

internal static class Rc1AssetConversionTests
{
    public static void Run()
    {
        VerifyMipmaps();
        Rc1TieLodLightingTests.Run();
        Rc1TieModelSplitTests.Run();
        VerifyMaterialBlendingState();
        VerifyShaderUploadSizeIsNotCullDistance();
        VerifyAlphaCulling();
        VerifyMixedMaterialCulling();
        foreach (var fat in new[] { false, true })
        {
            var source = Fixture(fat);
            var original = source.ToArray();
            var tie = TieClassReader.Read(source, TieClassReadOptions.ForGameProfile(Rc1TieGameProfile.Default));
            Expect(tie.LodTopologies[0].Triangles.Count == 2, "RC1 fixture has two triangles");
            var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
            Expect(source.AsSpan().SequenceEqual(original), "conversion preserves its input");
            var target = TieClassReader.Read(converted, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
            Expect((ushort)target.Header.OClass == 0x1234 && target.Header.Scale == tie.Header.Scale, "target class and scale");
            Expect(target.VertexNormals.Count == 64 && target.RgbaRemapOperations.Count > 0, "RC1 lighting palette becomes UYA remap groups");
            foreach (var block in target.PacketDataBlocks)
            {
                GcTieVuUploadAssertions.Validate(block);
                Expect(block.StripControls.All(strip => strip.TokenCount % 2 == 0), "UYA culled strips are even");
            }
            Expect(target.PacketTables.SelectMany(table => table.Packets).All(packet => packet.BfcDistance == 127), "backface culling stays enabled");
            foreach (var game in new[] { GameId.RC1, GameId.UYA })
            {
                var preview = FrontendAssetPackageBuilder.Build(game, FrontendAssetKind.Tie,
                    game == GameId.RC1 ? source : converted, []);
                Expect(preview.Entries.Any(entry => entry.Path == "model.gltf"), "source and converted previews");
            }
        }
        var moby = MobyModelPacker.Build(new() { Scale = 1 });
        _ = MobyModelReader.Read(new MemoryStream(LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA,
            FrontendAssetKind.Moby, moby, 0x1234)));
        var animated = new MobyModel { Scale = 1 };
        animated.Sequences.AddRange([new() { Sound = 2 }, new() { Sound = 3 }, new() { Sound = 5 }]);
        var withGap = MobyModelPacker.Build(animated);
        withGap.AsSpan(0x4c, 4).Clear();
        var convertedAnimation = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Moby, withGap, 0x1234);
        Expect(convertedAnimation[12] == 3 && BinaryPrimitives.ReadInt32LittleEndian(convertedAnimation.AsSpan(0x4c)) == 0,
            "null animation slots retain their source IDs");
        var sequences = MobyModelReader.Read(new MemoryStream(convertedAnimation)).Sequences;
        Expect(sequences.Select(sequence => sequence.Sound).SequenceEqual(new byte[] { 2, 5 }), "animations following a null slot retain their sounds");
        var shrub = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Shrub, new byte[0xc0], 0x1234);
        Expect(BinaryPrimitives.ReadUInt16LittleEndian(shrub.AsSpan(0x24)) == 0x1234, "shrub source class");
        var bank = SoundBankReaderTests.CreateDirectBank();
        var end = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(16)) + BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(20));
        var unpacked = bank[..end];
        var inventory = SoundBankReader.Inspect(GameId.RC1, unpacked);
        Expect(inventory.Programs.Select(program => program.ContentId).SequenceEqual(
            SoundBankReader.Inspect(GameId.UYA, bank).Programs.Select(program => program.ContentId)), "unpadded bank identities");
        Expect(SoundBankReader.RenderPreview(GameId.RC1, unpacked, 0).Pcm16.SequenceEqual(
            SoundBankReader.RenderPreview(GameId.UYA, bank, 0).Pcm16), "RC1 sound preview");
        var invalid = unpacked.Concat(new byte[] { 0 }).ToArray();
        try { SoundBankReader.Inspect(GameId.RC1, invalid); throw new Exception("Expected padding rejection"); }
        catch (InvalidDataException) { }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, Fixture(), 1, cancellation.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("RC1 previews, native conversion, VU uploads, culling, lighting, unpadded sounds and cancellation passed.");
    }

    private static void VerifyMaterialBlendingState()
    {
        var source = Fixture();
        var sourceShader = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x2c));
        var registers = new ulong[] { 6, 0x14, 0x34, 8, 0x36 };
        for (var index = 0; index < registers.Length; index++)
            BinaryPrimitives.WriteUInt64LittleEndian(source.AsSpan(sourceShader + index * 16 + 8), registers[index]);
        var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
        var shader = BinaryPrimitives.ReadInt32LittleEndian(converted.AsSpan(0x1c));
        Expect(source.AsSpan(sourceShader, 64).SequenceEqual(converted.AsSpan(shader, 64)),
            "texture, mip and clamp registers survive material conversion");
        foreach (var inheritedAlpha in new ulong[] { 0x8000000020, 0x8000000058 })
        {
            var state = new Dictionary<ulong, ulong> { [0x42] = inheritedAlpha };
            for (var index = 0; index < registers.Length; index++)
                state[BinaryPrimitives.ReadUInt64LittleEndian(converted.AsSpan(shader + index * 16 + 8))]
                    = BinaryPrimitives.ReadUInt64LittleEndian(converted.AsSpan(shader + index * 16));
            Expect(state[0x42] == 0x8000000044,
                "RC1 materials establish UYA source-alpha blending independently of previous draws");
        }
        var previous = converted.ToArray();
        source.AsSpan(sourceShader, 80).CopyTo(previous.AsSpan(shader));
        var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234);
        Expect(repaired is not null && repaired.AsSpan().SequenceEqual(converted), "previous RC1 material state is repaired");
        previous[shader + 0x30] ^= 1;
        Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234) is null, "material repair preserves authored edits");
        BinaryPrimitives.WriteUInt64LittleEndian(source.AsSpan(sourceShader + 0x40), 0x8000000058);
        BinaryPrimitives.WriteUInt64LittleEndian(source.AsSpan(sourceShader + 0x48), 0x42);
        converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
        Expect(source.AsSpan(sourceShader, 80).SequenceEqual(converted.AsSpan(shader, 80)),
            "an explicit source blending register is preserved");
    }

    private static void VerifyShaderUploadSizeIsNotCullDistance()
    {
        foreach (var fat in new[] { false, true })
        {
            var source = Fixture(fat);
            source[0x105] = 5; // One RC1 shader occupies five quadwords.
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(4), 0x100);
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(8), 0x100);
            source[0x21] = source[0x22] = 1;
            var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, 0x1234);
            var target = TieClassReader.Read(converted);
            Expect(target.PacketTables.SelectMany(table => table.Packets).All(packet => packet.BfcDistance == 127),
                "RC1 shader upload size must not become distance-dependent UYA culling in any LOD");
            var previous = converted.ToArray();
            foreach (var table in target.PacketTables)
                foreach (var packet in table.Packets)
                    previous[(int)table.Offset + packet.PacketIndex * 16 + 5] = 5;
            var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234);
            Expect(repaired is not null && repaired.AsSpan().SequenceEqual(converted),
                "the old shader-size culling conversion can be repaired without touching independent edits");
            previous[0x4c] ^= 1;
            Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234) is null,
                "culling repair preserves independent model edits");
        }
    }

    private static void VerifyAlphaCulling()
    {
        var source = Fixture();
        // Exercise the same material in each LOD with a shared source packet table.
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(4), 0x100);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(8), 0x100);
        source[0x21] = source[0x22] = 1;
        var previous = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
        foreach (var alpha in new byte[] { 0, 64, 128 })
        {
            var palette = new byte[1024]; // The unused palette entries are transparent.
            palette[7] = alpha;
            var pif = RatchetPs2.Core.Textures.Pif.PifWriter.Write(RatchetPs2.Core.Textures.Pif.PifWriter.CreateIndexed8(
                32, 32, palette, Enumerable.Repeat((byte)1, 1024).ToArray()));
            FrontendAssetTexture[] textures = [new(0, pif)];
            var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234, textures: textures);
            var tie = TieClassReader.Read(converted, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
            Expect(tie.PacketTables.SelectMany(table => table.Packets).All(packet => packet.BfcDistance == (alpha < 128 ? 0 : 127)),
                "only materials with referenced transparent pixels disable culling, in every LOD");
            foreach (var block in tie.PacketDataBlocks) GcTieVuUploadAssertions.Validate(block);
            var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, previous, 0x1234, 0x1234, textures: textures);
            Expect(repaired is not null && repaired.AsSpan().SequenceEqual(converted), "previous RC1 conversions repair their culling");
            Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, converted, 0x1234, 0x1234, textures: textures)!.AsSpan().SequenceEqual(converted), "culling repair is idempotent");
            var edited = previous.ToArray(); edited[0x4c] ^= 1;
            Expect(LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.RC1, GameId.UYA,
                FrontendAssetKind.Tie, source, edited, 0x1234, 0x1234, textures: textures) is null, "independent model edits are preserved");
            if (alpha == 128) Expect(converted.AsSpan().SequenceEqual(previous), "unused alpha entries do not change opaque models");
        }
    }

    private static void VerifyMixedMaterialCulling()
    {
        const int packet = 0x110;
        var original = Fixture();
        var oldNormal = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(12));
        var normal = oldNormal + 64;
        var shader = normal + 512;
        var source = new byte[shader + 160];
        original.AsSpan(0, packet + 64).CopyTo(source);
        original.AsSpan(packet + 64, 64).CopyTo(source.AsSpan(packet + 64));
        original.AsSpan(packet + 64, 64).CopyTo(source.AsSpan(packet + 128));
        original.AsSpan(oldNormal, 512).CopyTo(source.AsSpan(normal));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(12), normal);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x2c), shader);
        source[0x23] = 2; source[0x104] = 2;
        source[0x106] = 5; source[0x107] = 2; source[0x109] = 8; source[0x10a] = 12; source[0x10b] = 2; source[0x10c] = 13;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(packet), 19);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(packet + 0x14), 80);
        source[packet + 0x23] = 2; source[packet + 0x28] = 20; source[packet + 0x2a] = 8;
        source[packet + 0x30] = 4; source[packet + 0x32] = 25;
        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(packet + 128 + index * 16 + 6), (short)(26 + index * 3));
            source[packet + 13 * 16 + index] = index == 0 ? (byte)7 : (byte)3;
            source[packet + 13 * 16 + 4 + index] = index == 0 ? (byte)26 : (byte)3;
        }
        source[packet + 13 * 16 + 8] = 0xf6;
        byte[] Texture(byte alpha)
        {
            var palette = new byte[1024]; palette[7] = alpha;
            return RatchetPs2.Core.Textures.Pif.PifWriter.Write(RatchetPs2.Core.Textures.Pif.PifWriter.CreateIndexed8(
                32, 32, palette, Enumerable.Repeat((byte)1, 1024).ToArray()));
        }
        var before = TieClassReader.Read(source, TieClassReadOptions.ForGameProfile(Rc1TieGameProfile.Default));
        Expect(before.LodTopologies[0].Strips.Select(strip => strip.ShaderIndex).SequenceEqual(new int?[] { 0, 1 }), "mixed source packet resolves both material slots");
        var converted = LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234,
            textures: [new(0, Texture(128)), new(0, Texture(0))]);
        var target = TieClassReader.Read(converted);
        Expect(target.PacketTables[0].Packets.Select(packet => packet.BfcDistance).SequenceEqual(new byte[] { 127, 0 }),
            "mixed packets split so opaque geometry retains culling");
        Expect(target.LodTopologies[0].Triangles.Count == before.LodTopologies[0].Triangles.Count
            && target.LodTopologies[0].Strips.Select(strip => strip.ShaderIndex).SequenceEqual(new int?[] { 0, 1 }), "splitting preserves triangle count and material order");
        foreach (var block in target.PacketDataBlocks) GcTieVuUploadAssertions.Validate(block);
    }

    private static void VerifyMipmaps()
    {
        var palette = new byte[0x800];
        for (var index = 0; index < 256; index++) palette[index * 4 + 3] = 128;
        palette.AsSpan(0x500, 8 * 8).Fill(3);
        palette.AsSpan(0x400, 4 * 4).Fill(4);
        var pixels = new byte[32 * 32 + 16 * 16];
        pixels.AsSpan(0, 32 * 32).Fill(1);
        pixels.AsSpan(32 * 32).Fill(2);
        for (short levels = 1; levels <= 4; levels++)
        {
            var definition = new LevelAssetTextureDefinition(0, 0, 32, 32, levels, 0, 5, 4);
            var pif = LevelAssetReader.BuildAssetTexture("tie", 0, definition, palette, pixels, 0,
                isSwizzled: false, useTextureFlags: false).PifBytes;
            var source = RatchetPs2.Core.Textures.Pif.PifReader.Read(pif);
            Expect(source.TotalMipLevels == levels && source.PixelData.All(value => value == 1), "RC1 mip count and base pixels");
            for (var mip = 0; mip < source.MipPixelData.Count; mip++)
                Expect(source.MipPixelData[mip].All(value => value == mip + 2), "authored RC1 mip pixels retain their level order");
            var native = RatchetPs2.Core.Textures.Pif.PifReader.Read(LevelAssetModelConverter.ConvertTexture(
                GameId.RC1, GameId.UYA, pif));
            Expect(native.TotalMipLevels == Math.Min(levels, (short)3), "UYA material mip count");
            for (var mip = 0; mip < native.MipPixelData.Count; mip++)
                Expect(native.MipPixelData[mip].SequenceEqual(source.MipPixelData[mip]), "UYA retains the authored half and quarter images");
        }
        var flagged = RatchetPs2.Core.Textures.Pif.PifReader.Read(LevelAssetReader.BuildAssetTexture("tie", 0,
            new(0, 0, 32, 32, 3, 0, 5, -1), palette, pixels, 0, isSwizzled: false).PifBytes);
        Expect(flagged.TotalMipLevels == 3, "later-game texture flags retain their existing meaning");
    }

    internal static byte[] Fixture(bool fat = false)
    {
        const int packet = 0x110;
        var vertices = fat ? 5 : 4;
        var remap = 4 + vertices;
        var scissor = remap + 1;
        var normal = packet + (scissor + 1) * 16;
        var shader = normal + 64 * 8;
        var bytes = new byte[shader + 80];
        I32(0, 0x100); I32(12, normal); bytes[0x20] = 1; bytes[0x23] = 1;
        I32(0x2c, shader); F32(0x40, 1); F32(0x3c, 2);
        I32(0x100, 16); bytes[0x104] = 1; bytes[0x105] = 127;
        bytes[0x106] = 4; bytes[0x107] = 1; bytes[0x108] = 4;
        bytes[0x109] = (byte)vertices; bytes[0x10a] = (byte)remap; bytes[0x10b] = fat ? (byte)3 : (byte)1;
        bytes[0x10c] = (byte)scissor; bytes[0x10d] = 1;
        bytes[packet + 0x20] = bytes[packet + 0x21] = 1; bytes[packet + 0x22] = 6;
        bytes[packet + 0x23] = 1; bytes[packet + 0x24] = 7;
        bytes[packet + 0x28] = fat ? (byte)8 : (byte)12;
        bytes[packet + 0x29] = fat ? (byte)12 : (byte)6;
        bytes[packet + 0x2a] = fat ? (byte)2 : (byte)4; bytes[packet + 0x2b] = fat ? (byte)2 : (byte)0;
        bytes[packet + 0x2c] = 4; bytes[packet + 0x2e] = 6;
        var points = new[] { (0, 0), (1024, 0), (0, 1024), (1024, 1024) };
        var cursor = packet + 64;
        for (var index = 0; index < 4; index++)
        {
            var isFat = fat && index >= 2;
            var position = cursor + (isFat ? 8 : 0);
            var uv = cursor + (isFat ? 16 : 8);
            I16(position, points[index].Item1); I16(position + 2, points[index].Item2);
            I16(cursor + 6, 7 + index * 3); I16(uv, points[index].Item1); I16(uv + 2, points[index].Item2); I16(uv + 4, 4096);
            bytes[packet + scissor * 16 + index] = index == 0 ? (byte)7 : (byte)3;
            cursor += isFat ? 24 : 16;
        }
        bytes[packet + scissor * 16 + 4] = 0xf6;
        for (var index = 0; index < 64; index++) I16(normal + index * 8 + 4, 32767);
        return bytes;
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void I16(int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), checked((short)value));
        void F32(int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    }

    private static void Expect(bool value, string message) { if (!value) throw new Exception(message); }
}
