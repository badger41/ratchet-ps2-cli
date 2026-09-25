using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaInstanceTransformEdit(
    int SourceIndex,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale);

public sealed record UyaCameraInstanceEdit(
    int SourceIndex,
    UyaVector3 Position,
    UyaQuaternion Rotation);

public sealed record UyaSplineInstanceEdit(
    int SourceIndex,
    IReadOnlyList<UyaVector4> Points,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale);

public static class UyaShapeInstancesWriter
{
    public static byte[] WriteCuboids(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        var count = GameplayGeometryReader.ReadCuboids(source).Length;
        return Write(source, edits, count, "cuboid");
    }

    public static byte[] WriteSpheres(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        var count = GameplayGeometryReader.ReadShapes(source, "sphere").Length;
        return Write(source, edits, count, "sphere");
    }

    public static byte[] WriteCylinders(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        var count = GameplayGeometryReader.ReadShapes(source, "cylinder").Length;
        return Write(source, edits, count, "cylinder");
    }

    public static byte[] WritePills(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        var count = GameplayGeometryReader.ReadShapes(source, "pill").Length;
        return Write(source, edits, count, "pill");
    }

    private static byte[] Write(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaInstanceTransformEdit> edits,
        int count,
        string family)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.SourceIndex < 0 || edit.SourceIndex >= count || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException($"UYA {family} edit index {edit.SourceIndex} is invalid or duplicated.");
            UyaInstanceTransformWriter.WriteMatrixInverseRotation(
                output.AsSpan(0x10 + edit.SourceIndex * 0x80, 0x80), edit, family);
        }
        return output;
    }
}

public static class UyaCameraInstancesWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaCameraInstanceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var count = UyaCameraInstancesReader.Read(source).Count;
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.SourceIndex < 0 || edit.SourceIndex >= count || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException($"UYA camera edit index {edit.SourceIndex} is invalid or duplicated.");
            UyaInstanceTransformWriter.Validate(edit.Position, edit.Rotation, new(1, 1, 1), "camera");
            var record = output.AsSpan(
                UyaCameraInstancesReader.HeaderSize + edit.SourceIndex * UyaCameraInstancesReader.RecordSize,
                UyaCameraInstancesReader.RecordSize);
            UyaInstanceTransformWriter.WriteVector3(record, 4, edit.Position);
            UyaInstanceTransformWriter.WriteVector3(record, 0x10,
                UyaInstanceTransformWriter.ToZyxEuler(edit.Rotation));
        }
        return output;
    }
}

public static class UyaSoundInstancesWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var count = UyaSoundInstancesReader.Read(source).Count;
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.SourceIndex < 0 || edit.SourceIndex >= count || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException($"UYA sound edit index {edit.SourceIndex} is invalid or duplicated.");
            UyaInstanceTransformWriter.WriteMatrixInverseRotation(
                output.AsSpan(
                    UyaSoundInstancesReader.HeaderSize + edit.SourceIndex * UyaSoundInstancesReader.RecordSize + 0x10,
                    0x80),
                edit,
                "sound");
        }
        return output;
    }
}

public static class UyaSplineInstancesWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaSplineInstanceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var splines = GameplayGeometryReader.ReadSplines(source);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        var points = splines.Select(value => value.Points.Select(point =>
            new UyaVector4(point.X, point.Y, point.Z, point.W)).ToArray()).ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.SourceIndex < 0 || edit.SourceIndex >= splines.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException($"UYA spline edit index {edit.SourceIndex} is invalid or duplicated.");
            ArgumentNullException.ThrowIfNull(edit.Points);
            var transform = UyaInstanceTransformWriter.CreateMatrix(
                new(edit.SourceIndex, edit.Position, edit.Rotation, edit.Scale), "spline");
            points[edit.SourceIndex] = edit.Points.Select(point =>
            {
                var transformed = Vector3.Transform(new(point.X, point.Y, point.Z), transform);
                if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y)
                    || !float.IsFinite(transformed.Z) || !float.IsFinite(point.W))
                    throw new InvalidDataException("UYA spline transform produced a non-finite point.");
                return new UyaVector4(transformed.X, transformed.Y, transformed.Z, point.W);
            }).ToArray();
        }

        if (seen.All(index => points[index].Length == splines[index].Points.Length))
        {
            var patched = source.ToArray();
            foreach (var index in seen)
            {
                var pointOffset = checked(dataOffset
                    + BinaryPrimitives.ReadInt32LittleEndian(source[(0x10 + index * 4)..]) + 0x10);
                foreach (var point in points[index])
                {
                    UyaInstanceTransformWriter.WriteVector4(patched.AsSpan(pointOffset, 0x10), 0,
                        point.X, point.Y, point.Z, point.W);
                    pointOffset += 0x10;
                }
            }
            return patched;
        }

        var rebuiltDataSize = points.Sum(value => checked(0x10 + value.Length * 0x10));
        var output = new byte[checked(source.Length - dataSize + rebuiltDataSize)];
        source[..dataOffset].CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), rebuiltDataSize);
        var outputOffset = dataOffset;
        for (var index = 0; index < points.Length; index++)
        {
            var sourceOffset = checked(dataOffset
                + BinaryPrimitives.ReadInt32LittleEndian(source[(0x10 + index * 4)..]));
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x10 + index * 4), outputOffset - dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(outputOffset), points[index].Length);
            source.Slice(sourceOffset + 4, 0x0c).CopyTo(output.AsSpan(outputOffset + 4));
            outputOffset += 0x10;
            foreach (var point in points[index])
            {
                UyaInstanceTransformWriter.WriteVector4(output.AsSpan(outputOffset, 0x10), 0,
                    point.X, point.Y, point.Z, point.W);
                outputOffset += 0x10;
            }
        }
        source[(dataOffset + dataSize)..].CopyTo(output.AsSpan(outputOffset));
        return output;
    }
}

internal static class UyaInstanceTransformWriter
{
    public static void WriteMatrixInverseRotation(
        Span<byte> output,
        UyaInstanceTransformEdit edit,
        string family)
    {
        var matrix = CreateMatrix(edit, family);
        if (!Matrix4x4.Invert(matrix, out var inverse))
            throw new InvalidDataException($"UYA {family} transform is not invertible.");

        WriteVector3(output, 0x00, new(matrix.M11, matrix.M12, matrix.M13));
        WriteVector3(output, 0x10, new(matrix.M21, matrix.M22, matrix.M23));
        WriteVector3(output, 0x20, new(matrix.M31, matrix.M32, matrix.M33));
        WriteVector3(output, 0x30, edit.Position);
        WriteVector4(output, 0x40, inverse.M11, inverse.M12, inverse.M13, inverse.M14);
        WriteVector4(output, 0x50, inverse.M21, inverse.M22, inverse.M23, inverse.M24);
        WriteVector4(output, 0x60, inverse.M31, inverse.M32, inverse.M33, inverse.M34);
        WriteVector3(output, 0x70, ToZyxEuler(edit.Rotation));
    }

    public static Matrix4x4 CreateMatrix(UyaInstanceTransformEdit edit, string family)
    {
        Validate(edit.Position, edit.Rotation, edit.Scale, family);
        var rotation = Quaternion.Normalize(new(
            edit.Rotation.X, edit.Rotation.Y, edit.Rotation.Z, edit.Rotation.W));
        var matrix = Matrix4x4.CreateFromQuaternion(rotation);
        matrix.M11 *= edit.Scale.X;
        matrix.M12 *= edit.Scale.X;
        matrix.M13 *= edit.Scale.X;
        matrix.M21 *= edit.Scale.Y;
        matrix.M22 *= edit.Scale.Y;
        matrix.M23 *= edit.Scale.Y;
        matrix.M31 *= edit.Scale.Z;
        matrix.M32 *= edit.Scale.Z;
        matrix.M33 *= edit.Scale.Z;
        matrix.M41 = edit.Position.X;
        matrix.M42 = edit.Position.Y;
        matrix.M43 = edit.Position.Z;
        return matrix;
    }

    public static UyaVector3 ToZyxEuler(UyaQuaternion value)
    {
        var quaternion = Quaternion.Normalize(new(value.X, value.Y, value.Z, value.W));
        return new(
            MathF.Atan2(2 * (quaternion.W * quaternion.X + quaternion.Y * quaternion.Z),
                1 - 2 * (quaternion.X * quaternion.X + quaternion.Y * quaternion.Y)),
            MathF.Asin(Math.Clamp(2 * (quaternion.W * quaternion.Y - quaternion.Z * quaternion.X), -1f, 1f)),
            MathF.Atan2(2 * (quaternion.W * quaternion.Z + quaternion.X * quaternion.Y),
                1 - 2 * (quaternion.Y * quaternion.Y + quaternion.Z * quaternion.Z)));
    }

    public static void Validate(UyaVector3 position, UyaQuaternion rotation, UyaVector3 scale, string family)
    {
        var numbers = new[]
        {
            position.X, position.Y, position.Z,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            scale.X, scale.Y, scale.Z,
        };
        if (numbers.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"UYA {family} transform must contain finite values.");
        if (scale is { X: 0 } or { Y: 0 } or { Z: 0 })
            throw new InvalidDataException($"UYA {family} transform scale cannot contain zero.");
        var rotationLength = rotation.X * rotation.X + rotation.Y * rotation.Y
            + rotation.Z * rotation.Z + rotation.W * rotation.W;
        if (rotationLength == 0)
            throw new InvalidDataException($"UYA {family} transform rotation cannot be empty.");
    }

    public static void WriteVector3(Span<byte> destination, int offset, UyaVector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 4)..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 8)..], value.Z);
    }

    public static void WriteVector4(Span<byte> destination, int offset, float x, float y, float z, float w)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], x);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 4)..], y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 8)..], z);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + 12)..], w);
    }
}
