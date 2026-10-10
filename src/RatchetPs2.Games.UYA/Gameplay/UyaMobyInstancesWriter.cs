using System.Buffers.Binary;
using System.Numerics;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaMobyInstancesWriter
{
    public static byte[] WriteField(
        ReadOnlySpan<byte> source,
        int expectedClassId,
        UyaMobyInstanceFieldEdit edit)
    {
        var parsed = UyaMobyInstancesReader.ReadInstance(source);
        if (parsed.ClassId != expectedClassId)
            throw new InvalidDataException(
                $"UYA moby OClass changed from the expected value {expectedClassId} to {parsed.ClassId}.");
        UyaMobyInstanceFieldLimits.Validate(edit);

        var output = source[..UyaMobyInstancesReader.RecordSize].ToArray();
        var (offset, length) = edit.Field switch
        {
            UyaMobyInstanceField.Mission => (0x04, 4),
            UyaMobyInstanceField.Bolts => (0x14, 4),
            UyaMobyInstanceField.DrawDistance => (0x30, 4),
            UyaMobyInstanceField.UpdateDistance => (0x34, 4),
            UyaMobyInstanceField.IsRooted => (0x5c, 4),
            UyaMobyInstanceField.RootedDistance => (0x60, 4),
            UyaMobyInstanceField.Color => (0x74, 12),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };
        switch (edit.Value.Kind)
        {
            case UyaMobyInstanceFieldValueKind.Integer:
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), edit.Value.Integer!.Value);
                break;
            case UyaMobyInstanceFieldValueKind.Float:
                BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(offset), edit.Value.Float!.Value);
                break;
            case UyaMobyInstanceFieldValueKind.Boolean:
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), edit.Value.Boolean!.Value ? 1 : 0);
                break;
            case UyaMobyInstanceFieldValueKind.Color:
                var color = edit.Value.Color!.Value;
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), color.Red);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 4), color.Green);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 8), color.Blue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit));
        }

        var reread = UyaMobyInstancesReader.ReadInstance(output);
        if (reread.ClassId != expectedClassId)
            throw new InvalidDataException("UYA moby property edit changed the OClass.");
        var written = UyaMobyInstanceFieldLimits.Read(reread, edit.Field);
        if (written != edit.Value)
            throw new InvalidDataException($"UYA moby field {edit.Field} failed semantic re-read validation.");
        for (var index = 0; index < output.Length; index++)
            if ((index < offset || index >= offset + length) && output[index] != source[index])
                throw new InvalidDataException("UYA moby property edit changed an unowned record byte.");
        return output;
    }

    public static byte[] Write(UyaMobyInstances source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = CreateOutput(source, source.Instances.Count);
        for (var index = 0; index < source.Instances.Count; index++)
        {
            var bytes = source.Instances[index]?.RawBytes
                ?? throw new InvalidDataException("UYA moby instance raw bytes are missing.");
            if (bytes.Length != UyaMobyInstancesReader.RecordSize
                || BinaryPrimitives.ReadInt32LittleEndian(bytes) != UyaMobyInstancesReader.RecordSize)
                throw new InvalidDataException(
                    $"UYA moby instance must retain a valid 0x{UyaMobyInstancesReader.RecordSize:X}-byte record.");
            bytes.CopyTo(output.AsSpan(
                UyaMobyInstancesReader.HeaderSize + index * UyaMobyInstancesReader.RecordSize));
        }
        return output;
    }

    public static byte[] Write(
        UyaMobyInstances source,
        IReadOnlyList<UyaMobyInstanceEdit> instances)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(instances);
        var output = CreateOutput(source, instances.Count);
        for (var index = 0; index < instances.Count; index++)
            WriteRecord(output.AsSpan(UyaMobyInstancesReader.HeaderSize
                + index * UyaMobyInstancesReader.RecordSize), instances[index]);
        return output;
    }

    private static byte[] CreateOutput(UyaMobyInstances source, int count)
    {
        var recordsLength = checked(count * UyaMobyInstancesReader.RecordSize);
        var output = new byte[checked(UyaMobyInstancesReader.HeaderSize + recordsLength + source.TrailingBytes.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(output, count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), source.SpawnableMobyCount);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), source.Pad8);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12), source.PadC);
        source.TrailingBytes.CopyTo(output.AsSpan(UyaMobyInstancesReader.HeaderSize + recordsLength));
        return output;
    }

    private static void WriteRecord(Span<byte> output, UyaMobyInstanceEdit instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(instance.TemplateBytes);
        if (instance.TemplateBytes.Length != UyaMobyInstancesReader.RecordSize)
            throw new InvalidDataException($"UYA moby instance template must be 0x{UyaMobyInstancesReader.RecordSize:X} bytes.");
        if (instance.ClassId < 0) throw new InvalidDataException("UYA moby class ID cannot be negative.");
        var numbers = new[]
        {
            instance.Position.X, instance.Position.Y, instance.Position.Z,
            instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z, instance.Rotation.W,
            instance.Scale,
        };
        if (numbers.Any(number => !float.IsFinite(number)))
            throw new InvalidDataException("UYA moby transform must contain finite values.");
        if (instance.Scale == 0) throw new InvalidDataException("UYA moby scale cannot be zero.");
        var quaternion = new Quaternion(
            instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z, instance.Rotation.W);
        if (quaternion.LengthSquared() == 0) throw new InvalidDataException("UYA moby rotation cannot be empty.");
        quaternion = Quaternion.Normalize(quaternion);

        instance.TemplateBytes.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output, UyaMobyInstancesReader.RecordSize);
        BinaryPrimitives.WriteInt32LittleEndian(output[0x28..], instance.ClassId);
        BinaryPrimitives.WriteSingleLittleEndian(output[0x2c..], instance.Scale);
        WriteVector3(output, 0x40, instance.Position);
        WriteVector3(output, 0x4c, ToZyxEuler(quaternion));
    }

    private static UyaVector3 ToZyxEuler(Quaternion value)
    {
        var x = MathF.Atan2(2 * (value.W * value.X + value.Y * value.Z),
            1 - 2 * (value.X * value.X + value.Y * value.Y));
        var y = MathF.Asin(Math.Clamp(2 * (value.W * value.Y - value.Z * value.X), -1f, 1f));
        var z = MathF.Atan2(2 * (value.W * value.Z + value.X * value.Y),
            1 - 2 * (value.Y * value.Y + value.Z * value.Z));
        return new(x, y, z);
    }

    private static void WriteVector3(Span<byte> destination, int offset, UyaVector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 4)..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 8)..], value.Z);
    }
}

public sealed record UyaMobyInstanceEdit(
    int ClassId,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    float Scale,
    byte[] TemplateBytes);
