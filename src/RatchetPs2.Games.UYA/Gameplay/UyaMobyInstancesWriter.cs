using System.Buffers.Binary;
using System.Numerics;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaMobyInstancesWriter
{
    public static byte[] Write(
        UyaMobyInstances source,
        IReadOnlyList<UyaMobyInstanceEdit> instances)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(instances);
        var recordsLength = checked(instances.Count * UyaMobyInstancesReader.RecordSize);
        var output = new byte[checked(UyaMobyInstancesReader.HeaderSize + recordsLength + source.TrailingBytes.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(output, instances.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), source.SpawnableMobyCount);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), source.Pad8);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12), source.PadC);
        for (var index = 0; index < instances.Count; index++)
            WriteRecord(output.AsSpan(UyaMobyInstancesReader.HeaderSize
                + index * UyaMobyInstancesReader.RecordSize), instances[index]);
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
