using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.UYA.Moby;

public static class UyaMobyConverter
{
    public static byte[] ConvertFromCompact(MobyModel model, IReadOnlyList<MobyGltfAnimationClip> animations,
        CancellationToken cancellationToken = default, bool preserveSounds = true, bool normalizeRuntimeMetadata = true,
        bool rebuildSkeletonFallback = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(animations);
        if (model.AnimationFormat != MobyAnimationFormat.Compact || animations.Count != model.Sequences.Count)
            throw new InvalidDataException("UYA conversion requires all compact model animations to be decoded.");
        var sequences = new List<MobySequence>();
        for (var index = 0; index < animations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clip = animations[index];
            if (clip.SourceIndex != index || clip.Times.Length != model.Sequences[index].FrameCount + 1)
                throw new InvalidDataException($"Animation {index} has an inconsistent frame table.");
            var sequence = new MobySequence
            {
                Format = MobyAnimationFormat.Standard,
                BoundingSphere = model.Sequences[index].BoundingSphere,
                Sound = preserveSounds ? model.Sequences[index].Sound : byte.MaxValue,
                FormatMarker = byte.MaxValue,
            };
            if (preserveSounds) sequence.Triggers.AddRange(model.Sequences[index].Triggers);
            var scales = clip.Scales.OrderBy(pair => pair.Key).ToArray();
            var translations = clip.Translations.OrderBy(pair => pair.Key).ToArray();
            for (var frame = 0; frame < clip.Times.Length - 1; frame++)
                sequence.Frames.Add(EncodeFrame(model, clip, frame, scales, translations));
            sequences.Add(sequence);
        }
        if (model.JointCount > 0)
        {
            if (model.Skeleton is null || model.Skeleton.Bones.Count < model.JointCount
                || model.CommonTransforms is not { } transforms || transforms.Length < model.JointCount * 16)
                throw new InvalidDataException("Compact model has incomplete skeleton or common transforms.");
            foreach (var bone in model.Skeleton.Bones.Take(model.JointCount))
            {
                var a = bone.Row1; var b = bone.Row2; var c = bone.Row3;
                bone.Row1 = new() { X = a.X, Y = b.X, Z = c.X };
                bone.Row2 = new() { X = a.Y, Y = b.Y, Z = c.Y };
                bone.Row3 = new() { X = a.Z, Y = b.Z, Z = c.Z };
                bone.Row4 = new() { X = a.W, Y = b.W, Z = c.W, W = 1 };
            }
            for (var joint = 0; joint < model.JointCount; joint++)
            {
                var offset = joint * 16 + 12;
                var parent = transforms[offset] & 0x7f;
                if (parent == 0x7f) parent = joint;
                if (parent > joint) throw new InvalidDataException($"Compact joint {joint} has a forward parent reference.");
                // UYA uses the upper XYZ mask when falling back to common translations at animation LOD.
                var flags = normalizeRuntimeMetadata && parent != joint ? 0x70000000u : 0u;
                var parentFlags = checked((uint)(parent << 6)) | flags;
                BinaryPrimitives.WriteUInt32LittleEndian(transforms.AsSpan(offset), parentFlags);
                if (normalizeRuntimeMetadata && rebuildSkeletonFallback)
                {
                    var bone = model.Skeleton.Bones[joint];
                    var translation = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(transforms.AsSpan(joint * 16)),
                        BinaryPrimitives.ReadSingleLittleEndian(transforms.AsSpan(joint * 16 + 4)),
                        BinaryPrimitives.ReadSingleLittleEndian(transforms.AsSpan(joint * 16 + 8)));
                    if (parent == joint)
                    {
                        // The root common translation includes a distance bias; recover its bind position instead.
                        var inverseBind = new Matrix4x4(
                            bone.Row1.X, bone.Row1.Y, bone.Row1.Z, 0,
                            bone.Row2.X, bone.Row2.Y, bone.Row2.Z, 0,
                            bone.Row3.X, bone.Row3.Y, bone.Row3.Z, 0,
                            bone.Row4.X, bone.Row4.Y, bone.Row4.Z, 1);
                        if (!Matrix4x4.Invert(inverseBind, out var bind))
                            throw new InvalidDataException($"Compact root joint {joint} has a singular bind matrix.");
                        translation = bind.Translation;
                    }
                    // UYA's fourth column is fallback translation/parent data, not an affine matrix column.
                    bone.Row1.W = translation.X;
                    bone.Row2.W = translation.Y;
                    bone.Row3.W = translation.Z;
                    bone.Row4.W = BitConverter.UInt32BitsToSingle(parentFlags);
                }
            }
        }
        model.Sequences.Clear(); model.Sequences.AddRange(sequences);
        model.AnimationFormat = model.SkeletonFormat = MobyAnimationFormat.Standard;
        // DL marks its compact representation here; UYA uses these bits as a colour index.
        if (normalizeRuntimeMetadata)
        {
            model.ModeBits2 &= 0xef;
            // DL low-LOD packets still reference the full skeleton; UYA reads this byte as their joint count.
            if (model.LowLodMeshCount > 0) model.Padding = model.JointCount;
        }
        if (!preserveSounds) { model.SoundCount = 0; model.SoundDefOffset = 0; model.Sounds = null; }
        var bytes = MobyModelPacker.Build(model);
        using var stream = new MemoryStream(bytes, writable: false);
        var verified = MobyModelReader.Read(stream);
        var decoded = MobyStandardAnimationDecoder.Decode(verified);
        if (verified.JointCount > 0 && (decoded.Failures.Count != 0 || decoded.Animations.Count != animations.Count))
            throw new InvalidDataException($"Converted UYA animations failed readback validation: {string.Join("; ", decoded.Failures.Select(failure => $"{failure.SourceIndex}: {failure.Reason}"))}; decoded {decoded.Animations.Count}/{animations.Count}.");
        return bytes;
    }

    public static bool MatchesPreviousLodJointCount(ReadOnlySpan<byte> sourceBytes,
        ReadOnlySpan<byte> expectedBytes, ReadOnlySpan<byte> convertedBytes) =>
        sourceBytes.Length > 9 && expectedBytes.Length > 9 && convertedBytes.Length == expectedBytes.Length
        && convertedBytes[9] == sourceBytes[9]
        && convertedBytes[..9].SequenceEqual(expectedBytes[..9])
        && convertedBytes[10..].SequenceEqual(expectedBytes[10..]);

    private static MobyAnimationFrame EncodeFrame(MobyModel model, MobyGltfAnimationClip clip, int frame,
        KeyValuePair<int, Vector3[]>[] scales, KeyValuePair<int, Vector3[]>[] translations)
    {
        var rotationsSize = model.JointCount * 8;
        var translationOffset = rotationsSize + scales.Length * 8;
        var length = checked((translationOffset + translations.Length * 8 + 15) & ~15);
        if (length > byte.MaxValue * 16) throw new InvalidDataException("Animation frame exceeds the UYA size limit.");
        var bytes = new byte[length];
        for (var joint = 0; joint < model.JointCount; joint++)
        {
            var rotation = clip.Rotations.TryGetValue(joint, out var track) ? track[frame] : Quaternion.Identity;
            rotation = Quaternion.Normalize(rotation);
            WriteVector(bytes, joint * 8, new(-rotation.X, rotation.Z, -rotation.Y), 32767);
            WriteShort(bytes, joint * 8 + 6, rotation.W * 32767);
        }
        for (var index = 0; index < scales.Length; index++)
        {
            var value = scales[index].Value[frame];
            var offset = rotationsSize + index * 8;
            WriteVector(bytes, offset, new(value.X, value.Z, value.Y), 4096);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 6), checked((ushort)scales[index].Key));
        }
        for (var index = 0; index < translations.Length; index++)
        {
            var value = translations[index].Value[frame];
            var offset = translationOffset + index * 8;
            WriteVector(bytes, offset, new(value.X, -value.Z, value.Y), 1024 / model.Scale);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 6), checked((ushort)translations[index].Key));
        }
        var duration = clip.Times[frame + 1] - clip.Times[frame];
        if (!float.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Animation frame has invalid duration.");
        var rate = BitConverter.SingleToUInt32Bits(1 / (60 * duration));
        var timestamp = unchecked((ushort)MathF.Round(clip.Times[frame] * 480));
        return new()
        {
            Unknown00 = (byte)rate, Unknown01 = (byte)(rate >> 8), Unknown02 = (byte)(rate >> 16), Unknown03 = (byte)(rate >> 24),
            Unknown04 = (byte)timestamp, Unknown05 = (byte)(timestamp >> 8),
            Unknown08 = rotationsSize | scales.Length << 16,
            Unknown0C = translationOffset | translations.Length << 16,
            FrameData = bytes,
        };
    }

    private static void WriteVector(Span<byte> bytes, int offset, Vector3 value, float scale)
    {
        WriteShort(bytes, offset, value.X * scale);
        WriteShort(bytes, offset + 2, value.Y * scale);
        WriteShort(bytes, offset + 4, value.Z * scale);
    }

    private static void WriteShort(Span<byte> bytes, int offset, float value)
    {
        if (!float.IsFinite(value) || value < short.MinValue || value > short.MaxValue)
            throw new InvalidDataException("Animation transform exceeds the UYA fixed-point range.");
        BinaryPrimitives.WriteInt16LittleEndian(bytes[offset..], checked((short)MathF.Round(value)));
    }
}
