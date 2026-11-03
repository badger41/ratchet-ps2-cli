using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.UYA.Moby;
using RatchetPs2.Sdk;

internal static class LevelAssetModelConversionTests
{
    public static void Run()
    {
        CheckLinearTextureAllocation();
        CheckSkeletonFallback();
        CheckRotatedRootFallback();
        var palette = Enumerable.Range(0, 1024).Select(index => (byte)(index * 13)).ToArray();
        var smallestMip = Enumerable.Range(0, 64).Select(index => (byte)(index * 3 + 1)).ToArray();
        var swizzled = PifWriter.CreateIndexed8(32, 32, palette,
            Enumerable.Range(0, 1024).Select(index => (byte)(index * 7)).ToArray(),
            [Enumerable.Range(0, 256).Select(index => (byte)index).ToArray(), smallestMip], isSwizzled: true);
        var pifBytes = PifWriter.Write(swizzled);
        var unswizzled = PifReader.Read(LevelAssetModelConverter.ConvertTexture(GameId.DL, GameId.UYA, pifBytes));
        Check(!unswizzled.IsSwizzled && unswizzled.TotalMipLevels == 3, "UYA texture layout retains the linear smallest mip");
        Check(unswizzled.MipPixelData[1].SequenceEqual(smallestMip), "smallest GS-bank mip is preserved byte for byte");
        Check(TextureConverter.Decode(swizzled).PixelData.SequenceEqual(TextureConverter.Decode(unswizzled).PixelData), "texture colors preserved");
        var sourceMip = PifWriter.CreateIndexed8(16, 16, palette, swizzled.MipPixelData[0], isSwizzled: true);
        var targetMip = PifWriter.CreateIndexed8(16, 16, palette, unswizzled.MipPixelData[0]);
        Check(TextureConverter.Decode(sourceMip).PixelData.SequenceEqual(TextureConverter.Decode(targetMip).PixelData), "mipmap colors preserved");
        Check(PifReader.Read(pifBytes).IsSwizzled, "source texture remains unchanged");
        var previous = PifWriter.Write(new PifTextureData(unswizzled.Header, unswizzled.Encoding,
            unswizzled.PaletteData, unswizzled.PixelData, [unswizzled.MipPixelData[0]]));
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, pifBytes, previous)!
            .SequenceEqual(PifWriter.Write(unswizzled)), "repair restores previously omitted smallest mips");
        var larger = PifWriter.CreateIndexed8(64, 64, palette, new byte[4096], [new byte[1024],
            Enumerable.Range(0, 256).Select(index => (byte)index).ToArray()], isSwizzled: true);
        var largerResult = PifReader.Read(LevelAssetModelConverter.ConvertTexture(GameId.DL, GameId.UYA, PifWriter.Write(larger)));
        Check(largerResult.MipPixelData[1].SequenceEqual(larger.MipPixelData[1]), "GS-bank mip stays linear even at swizzlable dimensions");
        var previousSmallest = new byte[256];
        for (var index = 0; index < 256; index++) previousSmallest[TextureConverter.RemapPixelIndexFromRac4(index, 16)] = larger.MipPixelData[1][index];
        var previousLarger = PifWriter.Write(new PifTextureData(largerResult.Header, largerResult.Encoding,
            largerResult.PaletteData, largerResult.PixelData, [largerResult.MipPixelData[0], previousSmallest]));
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, PifWriter.Write(larger), previousLarger)!
            .SequenceEqual(PifWriter.Write(largerResult)), "repair corrects previously de-swizzled GS-bank mips");
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, pifBytes, PifWriter.Write(unswizzled)) is null,
            "texture repair is stable");
        previous[0x420] ^= 1;
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, pifBytes, previous) is null,
            "texture repair preserves independently edited images");
        var tie = new byte[0x160];
        BinaryPrimitives.WriteUInt32LittleEndian(tie.AsSpan(0x34), 0x80);
        BinaryPrimitives.WriteInt16LittleEndian(tie.AsSpan(0x38), 2);
        tie[0x90] = 12; tie[0x98] = 34;
        for (var lod = 0; lod < 3; lod++)
        {
            var chunk = 0xa0 + lod * 0x40;
            BinaryPrimitives.WriteUInt16LittleEndian(tie.AsSpan(0x2a + lod * 2), (ushort)(chunk - 0x80));
            tie[chunk] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(tie.AsSpan(chunk + 0x20), 4);
            BinaryPrimitives.WriteUInt16LittleEndian(tie.AsSpan(chunk + 0x2c), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tie.AsSpan(chunk + 0x2e), 0x20);
            BinaryPrimitives.WriteUInt16LittleEndian(tie.AsSpan(chunk + 0x30), 4);
        }
        var original = tie.ToArray();
        var converted = LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Tie, tie, 0x9001);
        var read = TieClassReader.Read(converted, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        Check(read.Header.VertexNormalsOffset == 0x80 && read.VertexNormals[0].X == 12
            && read.VertexNormals[1].X == 34 && unchecked((ushort)read.Header.OClass) == 0x9001, "TIE normal table and target class");
        Check(read.RgbaRemapOperations.Count == 3 && read.RgbaRemapOperations.All(operation => operation.SourceSlots.SequenceEqual([1])),
            "all LOD color remaps retain their source normal indices");
        Check(converted.AsSpan(0xa0).SequenceEqual(original.AsSpan(0xa0))
            && converted.AsSpan(0x2a, 6).SequenceEqual(original.AsSpan(0x2a, 6)), "remap bytes and relative addresses remain unchanged");
        Check(tie.SequenceEqual(original), "source TIE remains unchanged");
        var legacy = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(0x34), 0x90);
        BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(0x44), 0x9001);
        Check(converted.SequenceEqual(LevelAssetModelConverter.RepairPreviousTieConversion(GameId.DL, GameId.UYA, tie, legacy, 0x9001)!),
            "previous incorrect conversions are repaired");
        Check(LevelAssetModelConverter.RepairPreviousTieConversion(GameId.DL, GameId.UYA, tie, converted, 0x9001) is null,
            "repaired conversions are stable");
        legacy[0x48] = 7;
        Check(LevelAssetModelConverter.RepairPreviousTieConversion(GameId.DL, GameId.UYA, tie, legacy, 0x9001) is null,
            "repair preserves independently edited models");
        var shrub = LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Shrub, new byte[0xc0], 0x9002);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(shrub.AsSpan(0x24)) == 0x9002, "shrub target class");
        var model = CreateAnimatedModel();
        var expected = MobyStandardAnimationDecoder.Decode(model).Animations.Single();
        DlMobyConverter.ConvertFromUya(model);
        model.ModeBits2 = 0x18;
        var dl = MobyModelPacker.Build(model);
        var uya = LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Moby, dl, 0x9003);
        using var input = new MemoryStream(uya);
        var actualModel = MobyModelReader.Read(input);
        var decoded = MobyStandardAnimationDecoder.Decode(actualModel);
        Check(actualModel.ModeBits2 == 8, "DL representation flag does not select a UYA colour variant");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(actualModel.CommonTransforms!.AsSpan(28)) == 0x70000000,
            "non-root common translations retain the UYA XYZ mask for animation LOD fallback");
        Check(decoded.Failures.Count == 0 && decoded.Animations.Count == 1, "moby standard animation readback");
        var actual = decoded.Animations.Single();
        Check(actual.Times.Zip(expected.Times).All(pair => Math.Abs(pair.First - pair.Second) < 0.0001), "animation timing");
        Check(actual.Rotations.All(pair => pair.Value.Zip(expected.Rotations[pair.Key])
            .All(values => Math.Abs(Quaternion.Dot(values.First, values.Second)) > 0.9999)), "animation rotations");
        Check(actual.Translations.All(pair => pair.Value.Zip(expected.Translations[pair.Key])
            .All(values => Vector3.Distance(values.First, values.Second) < 0.01)), "animation translations");
        Check(actual.Scales.All(pair => pair.Value.Zip(expected.Scales[pair.Key])
            .All(values => Vector3.Distance(values.First, values.Second) < 0.001)), "animation scales");
        Check(actualModel.Skeleton!.Bones[1].Row4.X == 1 && actualModel.SoundCount == 1
            && actualModel.Sounds![0].MaxRange == 20 && actualModel.Sequences[0].Sound == 0
            && actualModel.Sequences[0].Triggers.Single().Unknown02 == 2, "skeleton layout, sound definitions and animation triggers");
        Check(uya.SequenceEqual(LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Moby, dl, 0x9003)), "deterministic conversion");
        using var legacyInput = new MemoryStream(dl);
        var previousModel = MobyModelReader.Read(legacyInput, new() { AnimationFormat = MobyAnimationFormat.Compact });
        var silentMoby = UyaMobyConverter.ConvertFromCompact(previousModel, [expected], preserveSounds: false, normalizeRuntimeMetadata: false);
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, silentMoby)!.SequenceEqual(uya),
            "previous silent conversions regain sound definitions and triggers");
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, uya) is null,
            "sound repair is idempotent");
        using var fallbackInput = new MemoryStream(dl);
        var fallbackModel = MobyModelReader.Read(fallbackInput, new() { AnimationFormat = MobyAnimationFormat.Compact });
        var legacyFallback = UyaMobyConverter.ConvertFromCompact(fallbackModel, [expected], rebuildSkeletonFallback: false);
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, legacyFallback)!.SequenceEqual(uya),
            "previous runtime conversions regain skeleton fallback data");
        legacyFallback[0x2d] ^= 1;
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, legacyFallback) is null,
            "skeleton fallback repair preserves independently edited models");
        using var legacySoundInput = new MemoryStream(dl);
        var legacySoundModel = MobyModelReader.Read(legacySoundInput, new() { AnimationFormat = MobyAnimationFormat.Compact });
        var legacySounds = UyaMobyConverter.ConvertFromCompact(legacySoundModel, [expected], normalizeRuntimeMetadata: false);
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, legacySounds)!.SequenceEqual(uya),
            "previous audible conversions regain runtime metadata without losing sound or animation data");
        legacySounds[0x2d] ^= 1;
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, legacySounds) is null,
            "runtime metadata repair preserves independently edited models");
        silentMoby[0x10] ^= 1;
        Check(LevelAssetModelConverter.RepairPreviousMobyConversion(GameId.DL, GameId.UYA, dl, silentMoby) is null,
            "sound repair preserves independently edited models");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Tie, tie, 0x9001, cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        try { LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, tie, 0x9001); throw new Exception("Expected rejection"); }
        catch (NotSupportedException) { }
        Console.WriteLine("DL to UYA model conversion checks passed.");
    }

    private static void CheckLinearTextureAllocation()
    {
        var valid = PifWriter.Write(PifWriter.CreateIndexed8(128, 128,
            Enumerable.Range(0, 1024).Select(index => (byte)(index * 13)).ToArray(),
            Enumerable.Range(0, 16384).Select(index => (byte)index).ToArray(),
            [new byte[4096], new byte[1024], new byte[256]]));
        var texture = PifReader.Read(valid);
        var target = PifWriter.Write(new PifTextureData(texture.Header, texture.Encoding, texture.PaletteData,
            texture.PixelData, texture.MipPixelData.Take(2).ToArray()));
        var embedded = valid.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(embedded.AsSpan(4), 6643361);
        var before = embedded.ToArray();
        var converted = LevelAssetModelConverter.ConvertTexture(GameId.DL, GameId.UYA, embedded);
        Check(converted.SequenceEqual(target), "linear mission materials retain their palette, pixels and the half/quarter mips supported by UYA");
        Check(embedded.SequenceEqual(before), "linear texture conversion preserves the source bytes");
        Check(LevelAssetModelConverter.ConvertTexture(GameId.DL, GameId.UYA, embedded, TextureRole.Billboard).SequenceEqual(valid),
            "UYA shrub billboards retain their supported fourth level");
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, embedded, before, TextureRole.Billboard)!
            .SequenceEqual(valid), "billboard allocation repair retains all supported mips");
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, embedded, valid)!.SequenceEqual(target),
            "previous size-normalized materials also regain the supported UYA mip count");
        var inventory = TextureInventoryBuilder.Build([new("mission", TextureAssetFamily.Moby, 0x209a, 0,
            TextureRole.Material, converted)]);
        Check(inventory.Textures.Single().MipLevelCount == 3, "converted linear mission texture passes strict build inventory validation");
        try
        {
            TextureInventoryBuilder.Build([new("mission", TextureAssetFamily.Moby, 0x209a, 0, TextureRole.Material, embedded)]);
            throw new Exception("Expected invalid allocation rejection");
        }
        catch (InvalidDataException) { }
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, embedded, before)!.SequenceEqual(target),
            "previous unchanged linear texture conversions are repaired");
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, embedded, converted) is null,
            "linear texture allocation repair is idempotent");
        before[0x420] ^= 1;
        Check(LevelAssetModelConverter.RepairPreviousTextureConversion(GameId.DL, GameId.UYA, embedded, before) is null,
            "linear texture allocation repair preserves edited pixels");
    }

    private static void CheckSkeletonFallback()
    {
        var local = new[] { new Vector3(3, 4, 5), new Vector3(10, 2, -1), new Vector3(-3, 7, 4) };
        var world = new[] { local[0], local[0] + local[1], local[0] + local[1] + local[2] };
        var model = new MobyModel { Scale = 1, JointCount = 3, CommonTransforms = new byte[48], Skeleton = new() };
        for (var joint = 0; joint < 3; joint++)
        {
            var parent = joint == 2 ? 1 : 0;
            var flags = joint == 0 ? 0u : 0x70000000u | (uint)(parent * 64);
            model.Skeleton.Bones.Add(new()
            {
                Row1 = new() { X = 1, W = local[joint].X },
                Row2 = new() { Y = 1, W = local[joint].Y },
                Row3 = new() { Z = 1, W = local[joint].Z },
                Row4 = new() { X = -world[joint].X, Y = -world[joint].Y, Z = -world[joint].Z,
                    W = BitConverter.UInt32BitsToSingle(flags) },
            });
            BinaryPrimitives.WriteSingleLittleEndian(model.CommonTransforms.AsSpan(joint * 16), local[joint].X);
            BinaryPrimitives.WriteSingleLittleEndian(model.CommonTransforms.AsSpan(joint * 16 + 4), local[joint].Y);
            BinaryPrimitives.WriteSingleLittleEndian(model.CommonTransforms.AsSpan(joint * 16 + 8),
                local[joint].Z + (joint == 0 ? 0.01024f : 0));
            BinaryPrimitives.WriteUInt32LittleEndian(model.CommonTransforms.AsSpan(joint * 16 + 12), flags);
        }
        DlMobyConverter.ConvertFromUya(model);
        var dl = MobyModelPacker.Build(model);
        var bytes = LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Moby, dl, 0x2079);
        using var input = new MemoryStream(bytes);
        var converted = MobyModelReader.Read(input);
        var fallbackWorld = new Vector3[3];
        for (var joint = 0; joint < 3; joint++)
        {
            var bone = converted.Skeleton!.Bones[joint];
            var flags = BitConverter.SingleToUInt32Bits(bone.Row4.W);
            Check(flags == (joint == 0 ? 0u : 0x70000000u | (uint)((joint == 2 ? 1 : 0) * 64)),
                "UYA skeleton fallback retains translation masks and byte-offset parent references");
            fallbackWorld[joint] = new(bone.Row1.W, bone.Row2.W, bone.Row3.W);
            if (joint > 0) fallbackWorld[joint] += fallbackWorld[(flags & 0xffff) / 64];
            var inverseBindTranslation = new Vector3(bone.Row4.X, bone.Row4.Y, bone.Row4.Z);
            var vertex = new Vector3(20, 30, 40);
            Check(Vector3.Distance(vertex + inverseBindTranslation + fallbackWorld[joint], vertex) < 0.0001f,
                "distance fallback keeps skinned vertices in their bind pose, including a nonzero root and a chained parent");
        }
    }

    private static void CheckRotatedRootFallback()
    {
        var position = new Vector3(3, 4, 5);
        var world = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(position);
        Check(Matrix4x4.Invert(world, out var inverseBind), "rotated fixture has an inverse bind");
        var model = new MobyModel { Scale = 1, JointCount = 1, Skeleton = new(), CommonTransforms = new byte[16],
            AnimationFormat = MobyAnimationFormat.Compact, SkeletonFormat = MobyAnimationFormat.Compact };
        model.Skeleton.Bones.Add(new()
        {
            Row1 = new() { X = inverseBind.M11, Y = inverseBind.M21, Z = inverseBind.M31, W = inverseBind.M41 },
            Row2 = new() { X = inverseBind.M12, Y = inverseBind.M22, Z = inverseBind.M32, W = inverseBind.M42 },
            Row3 = new() { X = inverseBind.M13, Y = inverseBind.M23, Z = inverseBind.M33, W = inverseBind.M43 },
        });
        model.CommonTransforms[12] = 0x7f;
        var bytes = LevelAssetModelConverter.Convert(GameId.DL, GameId.UYA, FrontendAssetKind.Moby,
            MobyModelPacker.Build(model), 0x2079);
        using var input = new MemoryStream(bytes);
        var bone = MobyModelReader.Read(input).Skeleton!.Bones[0];
        Check(Vector3.Distance(new(bone.Row1.W, bone.Row2.W, bone.Row3.W), position) < 0.0001f
            && BitConverter.SingleToUInt32Bits(bone.Row4.W) == 0,
            "root fallback recovers its bind position through rotation, without the common-table distance bias");
    }

    private static MobyModel CreateAnimatedModel()
    {
        var model = new MobyModel { Scale = 1, JointCount = 2, CommonTransforms = new byte[32], Skeleton = new(),
            Sounds = [new() { MinRange = 1, MaxRange = 20, Index = 0 }] };
        for (var i = 0; i < 2; i++) model.Skeleton.Bones.Add(new()
        {
            Row1 = new() { X = 1 }, Row2 = new() { Y = 1 }, Row3 = new() { Z = 1 }, Row4 = new() { X = i, W = 1 },
        });
        var sequence = new MobySequence { Sound = 0, FormatMarker = 255 };
        sequence.Triggers.Add(new() { Unknown00 = 1, Unknown02 = 2 });
        for (var frame = 0; frame < 2; frame++)
        {
            var data = new byte[32];
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 32767);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 32767);
            for (var axis = 0; axis < 3; axis++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(16 + axis * 2), 4096);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 1);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(24), (short)(100 + frame * 10));
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), -200);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 300);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(30), 1);
            sequence.Frames.Add(new() { Unknown02 = 128, Unknown03 = 63, Unknown04 = (byte)(frame * 8), Unknown08 = 16 | 1 << 16,
                Unknown0C = 24 | 1 << 16, FrameData = data });
        }
        sequence.FrameCount = 2; model.Sequences.Add(sequence);
        return model;
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
