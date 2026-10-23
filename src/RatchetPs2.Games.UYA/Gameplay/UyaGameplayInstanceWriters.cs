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

public sealed record UyaSplineInstanceComposition(
    IReadOnlyList<UyaVector4> Points,
    byte[] NativeHeader,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale);

public sealed record UyaGrindPathInstanceComposition(
    IReadOnlyList<UyaVector4> Points,
    byte[] NativeRecord,
    byte[] NativeHeader,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale);

public sealed record UyaAreaInstanceComposition(
    UyaVector4 BoundingSphere,
    short LastUpdateTime,
    IReadOnlyList<int> SplineIndices,
    IReadOnlyList<int> CuboidIndices,
    IReadOnlyList<int> SphereIndices,
    IReadOnlyList<int> CylinderIndices,
    IReadOnlyList<int> NegativeCuboidIndices);

public static class UyaShapeInstancesWriter
{
    public static byte[] ComposeCuboids(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaInstanceTransformEdit> instances) =>
        Compose(source, instances, GameplayGeometryReader.ReadCuboids(source).Length, "cuboid");

    public static byte[] ComposeSpheres(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaInstanceTransformEdit> instances) =>
        Compose(source, instances, GameplayGeometryReader.ReadShapes(source, "sphere").Length, "sphere");

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

    private static byte[] Compose(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaInstanceTransformEdit> instances,
        int sourceCount,
        string family)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (!instances.Select(value => value.SourceIndex).SequenceEqual(Enumerable.Range(0, instances.Count)))
            throw new InvalidDataException($"UYA {family} composition indexes must be contiguous.");
        var sourceRecordsEnd = checked(0x10 + sourceCount * 0x80);
        var outputRecordsEnd = checked(0x10 + instances.Count * 0x80);
        var output = new byte[checked(outputRecordsEnd + source.Length - sourceRecordsEnd)];
        source[..0x10].CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output, instances.Count);
        for (var index = 0; index < instances.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(instances[index]);
            UyaInstanceTransformWriter.WriteMatrixInverseRotation(
                output.AsSpan(0x10 + index * 0x80, 0x80), instances[index], family);
        }
        source[sourceRecordsEnd..].CopyTo(output.AsSpan(outputRecordsEnd));
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
    public static byte[] Compose(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaSplineInstanceComposition> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _ = GameplayGeometryReader.ReadSplines(source);
        var sourceDataOffset = source.IsEmpty ? 0x10 : BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        var sourceDataSize = source.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        var dataOffset = UyaSplineComposition.Align16(checked(0x10 + instances.Count * 4));
        var dataSize = instances.Sum(value => checked(0x10 + value.Points.Count * 0x10));
        var trailing = source.IsEmpty ? ReadOnlySpan<byte>.Empty : source[(sourceDataOffset + sourceDataSize)..];
        var output = new byte[checked(dataOffset + dataSize + trailing.Length)];
        if (!source.IsEmpty) source[..0x10].CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output, instances.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), dataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), dataSize);
        var outputOffset = dataOffset;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index] ?? throw new ArgumentNullException(nameof(instances));
            if (instance.NativeHeader is not { Length: 0x0c })
                throw new InvalidDataException($"UYA spline {index} native header must contain 12 bytes.");
            var points = UyaSplineComposition.TransformPoints(
                instance.Points, instance.Position, instance.Rotation, instance.Scale, index, "spline");
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x10 + index * 4), outputOffset - dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(outputOffset), points.Length);
            instance.NativeHeader.CopyTo(output.AsSpan(outputOffset + 4, 0x0c));
            outputOffset += 0x10;
            UyaSplineComposition.WritePoints(output, ref outputOffset, points);
        }
        trailing.CopyTo(output.AsSpan(outputOffset));
        var rebuilt = GameplayGeometryReader.ReadSplines(output);
        if (rebuilt.Length != instances.Count)
            throw new InvalidDataException("UYA spline composition did not re-read the expected count.");
        return output;
    }

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

public static class UyaGrindPathInstancesWriter
{
    public static byte[] Compose(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaGrindPathInstanceComposition> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _ = GameplayGeometryReader.ReadGrindPaths(source);
        var sourceDataOffset = source.IsEmpty ? 0x10 : BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        var sourceDataSize = source.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        var dataOffset = UyaSplineComposition.Align16(checked(0x10 + instances.Count * 0x24));
        var dataSize = instances.Sum(value => checked(0x10 + value.Points.Count * 0x10));
        var trailing = source.IsEmpty ? ReadOnlySpan<byte>.Empty : source[(sourceDataOffset + sourceDataSize)..];
        var output = new byte[checked(dataOffset + dataSize + trailing.Length)];
        if (!source.IsEmpty) source[..0x10].CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output, instances.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), dataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), dataSize);
        var offsetsOffset = 0x10 + instances.Count * 0x20;
        var outputOffset = dataOffset;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index] ?? throw new ArgumentNullException(nameof(instances));
            if (instance.NativeRecord is not { Length: 0x20 })
                throw new InvalidDataException($"UYA grind path {index} native record must contain 32 bytes.");
            if (instance.NativeHeader is not { Length: 0x0c })
                throw new InvalidDataException($"UYA grind path {index} native header must contain 12 bytes.");
            var points = UyaSplineComposition.TransformPoints(
                instance.Points, instance.Position, instance.Rotation, instance.Scale, index, "grind path");
            instance.NativeRecord.CopyTo(output.AsSpan(0x10 + index * 0x20, 0x20));
            WriteBounds(output.AsSpan(0x10 + index * 0x20, 0x10), points);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offsetsOffset + index * 4), outputOffset - dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(outputOffset), points.Length);
            instance.NativeHeader.CopyTo(output.AsSpan(outputOffset + 4, 0x0c));
            outputOffset += 0x10;
            UyaSplineComposition.WritePoints(output, ref outputOffset, points);
        }
        trailing.CopyTo(output.AsSpan(outputOffset));
        var rebuilt = GameplayGeometryReader.ReadGrindPaths(output);
        if (rebuilt.Length != instances.Count)
            throw new InvalidDataException("UYA grind-path composition did not re-read the expected count.");
        return output;
    }

    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaSplineInstanceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var paths = GameplayGeometryReader.ReadGrindPaths(source);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        var points = paths.Select(value => value.Points.Select(point =>
            new UyaVector4(point.X, point.Y, point.Z, point.W)).ToArray()).ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.SourceIndex < 0 || edit.SourceIndex >= paths.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException($"UYA grind-path edit index {edit.SourceIndex} is invalid or duplicated.");
            ArgumentNullException.ThrowIfNull(edit.Points);
            var transform = UyaInstanceTransformWriter.CreateMatrix(
                new(edit.SourceIndex, edit.Position, edit.Rotation, edit.Scale), "grind path");
            points[edit.SourceIndex] = edit.Points.Select(point =>
            {
                var transformed = Vector3.Transform(new(point.X, point.Y, point.Z), transform);
                if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y)
                    || !float.IsFinite(transformed.Z) || !float.IsFinite(point.W))
                    throw new InvalidDataException("UYA grind-path transform produced a non-finite point.");
                return new UyaVector4(transformed.X, transformed.Y, transformed.Z, point.W);
            }).ToArray();
        }

        var output = seen.All(index => points[index].Length == paths[index].Points.Length)
            ? source.ToArray()
            : Rebuild(source, points, dataOffset, dataSize);
        var offsetsOffset = 0x10 + paths.Length * 0x20;
        foreach (var index in seen)
        {
            var pointOffset = checked(dataOffset
                + BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(offsetsOffset + index * 4)) + 0x10);
            foreach (var point in points[index])
            {
                UyaInstanceTransformWriter.WriteVector4(output.AsSpan(pointOffset, 0x10), 0,
                    point.X, point.Y, point.Z, point.W);
                pointOffset += 0x10;
            }
            WriteBounds(output.AsSpan(0x10 + index * 0x20, 0x10), points[index]);
        }
        return output;
    }

    private static byte[] Rebuild(ReadOnlySpan<byte> source, IReadOnlyList<UyaVector4[]> points,
        int dataOffset, int dataSize)
    {
        var rebuiltDataSize = points.Sum(value => checked(0x10 + value.Length * 0x10));
        var output = new byte[checked(source.Length - dataSize + rebuiltDataSize)];
        source[..dataOffset].CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), rebuiltDataSize);
        var offsetsOffset = 0x10 + points.Count * 0x20;
        var outputOffset = dataOffset;
        for (var index = 0; index < points.Count; index++)
        {
            var sourceOffset = checked(dataOffset
                + BinaryPrimitives.ReadInt32LittleEndian(source[(offsetsOffset + index * 4)..]));
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offsetsOffset + index * 4), outputOffset - dataOffset);
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

    private static void WriteBounds(Span<byte> output, IReadOnlyList<UyaVector4> points)
    {
        if (points.Count == 0)
        {
            output.Clear();
            return;
        }
        var min = new Vector3(points.Min(value => value.X), points.Min(value => value.Y), points.Min(value => value.Z));
        var max = new Vector3(points.Max(value => value.X), points.Max(value => value.Y), points.Max(value => value.Z));
        var center = (min + max) / 2;
        var radius = points.Max(value => Vector3.Distance(center, new(value.X, value.Y, value.Z)));
        UyaInstanceTransformWriter.WriteVector4(output, 0, center.X, center.Y, center.Z, radius);
    }
}

public static class UyaAreaInstancesWriter
{
    private const int HeaderSize = 0x24;
    private const int RecordSize = 0x30;
    private const int PartCount = 5;

    public static byte[] Compose(
        ReadOnlySpan<byte> source,
        IReadOnlyList<UyaAreaInstanceComposition> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _ = GameplayGeometryReader.ReadAreas(source);
        var sourceDataSize = source.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(source);
        var trailing = source.IsEmpty ? ReadOnlySpan<byte>.Empty : source[(4 + sourceDataSize)..];
        var partSizes = new int[PartCount];
        foreach (var instance in instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            var parts = Parts(instance);
            for (var part = 0; part < PartCount; part++)
            {
                if (parts[part].Count > short.MaxValue)
                    throw new InvalidDataException($"UYA area part {part} exceeds the native link limit.");
                partSizes[part] = checked(partSizes[part] + parts[part].Count * sizeof(int));
            }
        }

        var recordsEnd = checked(HeaderSize + instances.Count * RecordSize);
        var partOffsets = new int[PartCount];
        var dataEnd = recordsEnd;
        for (var part = 0; part < PartCount; part++)
        {
            partOffsets[part] = dataEnd;
            dataEnd = checked(dataEnd + partSizes[part]);
        }
        var output = new byte[checked(dataEnd + trailing.Length)];
        if (!source.IsEmpty) source.Slice(0x1c, 8).CopyTo(output.AsSpan(0x1c));
        BinaryPrimitives.WriteInt32LittleEndian(output, dataEnd - 4);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), instances.Count);
        for (var part = 0; part < PartCount; part++)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8 + part * 4), partOffsets[part] - 4);

        var cursors = partOffsets.ToArray();
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            Validate(instance.BoundingSphere, index);
            var recordOffset = HeaderSize + index * RecordSize;
            UyaInstanceTransformWriter.WriteVector4(output.AsSpan(recordOffset, 0x10), 0,
                instance.BoundingSphere.X, instance.BoundingSphere.Y,
                instance.BoundingSphere.Z, instance.BoundingSphere.W);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(recordOffset + 0x1a), instance.LastUpdateTime);
            var parts = Parts(instance);
            for (var part = 0; part < PartCount; part++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(
                    output.AsSpan(recordOffset + 0x10 + part * 2), checked((short)parts[part].Count));
                BinaryPrimitives.WriteInt32LittleEndian(
                    output.AsSpan(recordOffset + 0x1c + part * 4), cursors[part] - partOffsets[part]);
                foreach (var value in parts[part])
                {
                    if (value < 0) throw new InvalidDataException($"UYA area {index} has a negative part {part} index.");
                    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(cursors[part]), value);
                    cursors[part] += sizeof(int);
                }
            }
        }
        trailing.CopyTo(output.AsSpan(dataEnd));
        if (GameplayGeometryReader.ReadAreas(output).Length != instances.Count)
            throw new InvalidDataException("UYA area composition did not re-read the expected count.");
        return output;
    }

    private static IReadOnlyList<int>[] Parts(UyaAreaInstanceComposition value) =>
    [
        value.SplineIndices ?? throw new InvalidDataException("UYA area spline links are missing."),
        value.CuboidIndices ?? throw new InvalidDataException("UYA area cuboid links are missing."),
        value.SphereIndices ?? throw new InvalidDataException("UYA area sphere links are missing."),
        value.CylinderIndices ?? throw new InvalidDataException("UYA area cylinder links are missing."),
        value.NegativeCuboidIndices ?? throw new InvalidDataException("UYA area negative-cuboid links are missing."),
    ];

    private static void Validate(UyaVector4 value, int index)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
            || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new InvalidDataException($"UYA area {index} bounds are not finite.");
    }
}

internal static class UyaSplineComposition
{
    public static int Align16(int value) => checked((value + 0x0f) & ~0x0f);

    public static UyaVector4[] TransformPoints(
        IReadOnlyList<UyaVector4> source,
        UyaVector3 position,
        UyaQuaternion rotation,
        UyaVector3 scale,
        int index,
        string family)
    {
        ArgumentNullException.ThrowIfNull(source);
        var transform = UyaInstanceTransformWriter.CreateMatrix(
            new(index, position, rotation, scale), family);
        return source.Select(point =>
        {
            var transformed = Vector3.Transform(new(point.X, point.Y, point.Z), transform);
            if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y)
                || !float.IsFinite(transformed.Z) || !float.IsFinite(point.W))
                throw new InvalidDataException($"UYA {family} transform produced a non-finite point.");
            return new UyaVector4(transformed.X, transformed.Y, transformed.Z, point.W);
        }).ToArray();
    }

    public static void WritePoints(byte[] output, ref int offset, IReadOnlyList<UyaVector4> points)
    {
        foreach (var point in points)
        {
            UyaInstanceTransformWriter.WriteVector4(
                output.AsSpan(offset, 0x10), 0, point.X, point.Y, point.Z, point.W);
            offset += 0x10;
        }
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
