using System.Buffers.Binary;
using System.Numerics;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaTieInstancesWriter
{
    public static byte[] Write(
        UyaTieInstances source,
        IReadOnlyList<UyaStaticInstanceEdit> instances) =>
        UyaStaticInstanceWriter.Write(
            source.HeaderWords, source.TrailingBytes, instances, UyaTieInstancesReader.RecordSize, "tie");
}

public static class UyaShrubInstancesWriter
{
    public static byte[] Write(
        UyaShrubInstances source,
        IReadOnlyList<UyaStaticInstanceEdit> instances) =>
        UyaStaticInstanceWriter.Write(
            source.HeaderWords, source.TrailingBytes, instances, UyaShrubInstancesReader.RecordSize, "shrub");
}

public sealed record UyaStaticInstanceEdit(
    int ClassId,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale,
    byte[] TemplateBytes);

public readonly record struct UyaQuaternion(float X, float Y, float Z, float W);

internal static class UyaStaticInstanceWriter
{
    public static byte[] Write(
        IReadOnlyList<int> headerWords,
        ReadOnlySpan<byte> trailingBytes,
        IReadOnlyList<UyaStaticInstanceEdit> instances,
        int recordSize,
        string family)
    {
        ArgumentNullException.ThrowIfNull(headerWords);
        ArgumentNullException.ThrowIfNull(instances);
        if (headerWords.Count != 3)
            throw new InvalidDataException($"UYA {family} instance header must contain three preserved words.");

        var recordsLength = checked(instances.Count * recordSize);
        var output = new byte[checked(UyaTieInstancesReader.HeaderSize + recordsLength + trailingBytes.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(output, instances.Count);
        for (var index = 0; index < headerWords.Count; index++)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4 + index * sizeof(int)), headerWords[index]);
        for (var index = 0; index < instances.Count; index++)
            WriteRecord(output.AsSpan(UyaTieInstancesReader.HeaderSize + index * recordSize, recordSize),
                instances[index], recordSize, family);
        trailingBytes.CopyTo(output.AsSpan(UyaTieInstancesReader.HeaderSize + recordsLength));
        return output;
    }

    private static void WriteRecord(
        Span<byte> output,
        UyaStaticInstanceEdit instance,
        int recordSize,
        string family)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(instance.TemplateBytes);
        if (instance.TemplateBytes.Length != recordSize)
            throw new InvalidDataException($"UYA {family} instance template must be 0x{recordSize:X} bytes.");
        if (instance.ClassId < 0)
            throw new InvalidDataException($"UYA {family} class ID cannot be negative.");
        Validate(instance, family);

        instance.TemplateBytes.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output, instance.ClassId);
        var rotation = Quaternion.Normalize(new(
            instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z, instance.Rotation.W));
        var matrix = Matrix4x4.CreateFromQuaternion(rotation);
        WriteVector3(output, 0x10, matrix.M11 * instance.Scale.X, matrix.M12 * instance.Scale.X,
            matrix.M13 * instance.Scale.X);
        WriteVector3(output, 0x20, matrix.M21 * instance.Scale.Y, matrix.M22 * instance.Scale.Y,
            matrix.M23 * instance.Scale.Y);
        WriteVector3(output, 0x30, matrix.M31 * instance.Scale.Z, matrix.M32 * instance.Scale.Z,
            matrix.M33 * instance.Scale.Z);
        WriteVector3(output, 0x40, instance.Position.X, instance.Position.Y, instance.Position.Z);
    }

    private static void Validate(UyaStaticInstanceEdit value, string family)
    {
        var numbers = new[]
        {
            value.Position.X, value.Position.Y, value.Position.Z,
            value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W,
            value.Scale.X, value.Scale.Y, value.Scale.Z,
        };
        if (numbers.Any(number => !float.IsFinite(number)))
            throw new InvalidDataException($"UYA {family} instance transform must contain finite values.");
        if (value.Scale is { X: 0 } or { Y: 0 } or { Z: 0 })
            throw new InvalidDataException($"UYA {family} instance scale cannot contain zero.");
        var rotationLength = value.Rotation.X * value.Rotation.X + value.Rotation.Y * value.Rotation.Y
            + value.Rotation.Z * value.Rotation.Z + value.Rotation.W * value.Rotation.W;
        if (rotationLength == 0)
            throw new InvalidDataException($"UYA {family} instance rotation cannot be empty.");
    }

    private static void WriteVector3(Span<byte> destination, int offset, float x, float y, float z)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], x);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 4)..], y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 8)..], z);
    }
}
