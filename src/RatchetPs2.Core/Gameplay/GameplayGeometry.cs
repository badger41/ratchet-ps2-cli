using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Gameplay;

public sealed record GameplayGeometry(
    GameplayCuboid[] Cuboids,
    GameplaySpline[] Splines,
    GameplayArea[] Areas);

public sealed record GameplayCuboid(
    int Index,
    float[] Matrix,
    float[] InverseRotationMatrix,
    GameplayVector3 Rotation);

public sealed record GameplaySpline(
    int Index,
    GameplayVector4[] Points);

public sealed record GameplayArea(
    int Index,
    GameplayVector4 BoundingSphere,
    short LastUpdateTime,
    int[] SplineIndices,
    int[] CuboidIndices,
    int[] SphereIndices,
    int[] CylinderIndices,
    int[] NegativeCuboidIndices);

public readonly record struct GameplayVector3(float X, float Y, float Z);

public readonly record struct GameplayVector4(float X, float Y, float Z, float W);

public static class GameplayGeometryReader
{
    public static GameplayGeometry Read(IReadOnlyList<GameplayRawBlock> blocks)
    {
        var cuboidBytes = FindPayload(blocks, "cuboids");
        var splineBytes = FindPayload(blocks, "splines");
        var areaBytes = FindPayload(blocks, "areas");
        return new GameplayGeometry(
            cuboidBytes.Length >= 0x10 ? ReadCuboids(cuboidBytes) : [],
            splineBytes.Length >= 0x10 ? ReadSplines(splineBytes) : [],
            areaBytes.Length >= 0x24 ? ReadAreas(areaBytes) : []);
    }

    public static GameplayCuboid[] ReadCuboids(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        EnsureRange(data, 0, 0x10, "gameplay cuboid header");
        var count = ReadNonNegativeInt32(data, 0, "gameplay cuboid count");
        EnsureRange(data, 0x10, checked(count * 0x80), "gameplay cuboid records");

        var cuboids = new GameplayCuboid[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 0x10 + (index * 0x80);
            cuboids[index] = new GameplayCuboid(
                index,
                ReadFloats(data, offset, 16),
                ReadFloats(data, offset + 0x40, 12),
                ReadVector3(data, offset + 0x70));
        }

        return cuboids;
    }

    public static GameplaySpline[] ReadSplines(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        EnsureRange(data, 0, 0x10, "gameplay spline header");
        var count = ReadNonNegativeInt32(data, 0, "gameplay spline count");
        var dataOffset = ReadNonNegativeInt32(data, 4, "gameplay spline data offset");
        var dataSize = ReadNonNegativeInt32(data, 8, "gameplay spline data size");
        EnsureRange(data, 0x10, checked(count * 4), "gameplay spline offset table");
        EnsureRange(data, dataOffset, dataSize, "gameplay spline data");
        if (0x10 + (count * 4) > dataOffset)
        {
            throw new InvalidDataException("Gameplay spline offset table overlaps spline data.");
        }

        var dataEnd = checked(dataOffset + dataSize);
        var splines = new GameplaySpline[count];
        for (var index = 0; index < count; index++)
        {
            var relativeOffset = ReadNonNegativeInt32(data, 0x10 + (index * 4), $"gameplay spline {index} offset");
            var offset = checked(dataOffset + relativeOffset);
            EnsureRange(data, offset, 0x10, $"gameplay spline {index} header");
            var pointCount = ReadNonNegativeInt32(data, offset, $"gameplay spline {index} point count");
            var pointsEnd = checked(offset + 0x10 + (pointCount * 0x10));
            EnsureRange(data, offset + 0x10, checked(pointCount * 0x10), $"gameplay spline {index} points");
            if (pointsEnd > dataEnd)
            {
                throw new InvalidDataException($"Gameplay spline {index} points extend beyond spline data.");
            }

            var points = new GameplayVector4[pointCount];
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                points[pointIndex] = ReadVector4(data, offset + 0x10 + (pointIndex * 0x10));
            }

            splines[index] = new GameplaySpline(index, points);
        }

        return splines;
    }

    public static GameplayArea[] ReadAreas(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        EnsureRange(data, 0, 0x24, "gameplay area header");
        var dataSize = ReadNonNegativeInt32(data, 0, "gameplay area data size");
        var dataEnd = checked(4 + dataSize);
        EnsureRange(data, 4, dataSize, "gameplay area data");
        var count = ReadNonNegativeInt32(data, 4, "gameplay area count");
        EnsureRange(data, 0x24, checked(count * 0x30), "gameplay area records");
        if (0x24 + (count * 0x30) > dataEnd)
        {
            throw new InvalidDataException("Gameplay area records extend beyond area data.");
        }

        var partOffsets = new int[5];
        for (var index = 0; index < partOffsets.Length; index++)
        {
            partOffsets[index] = ReadInt32LittleEndian(data, 8 + (index * 4));
        }
        var areas = new GameplayArea[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 0x24 + (index * 0x30);
            areas[index] = new GameplayArea(
                index,
                ReadVector4(data, offset),
                ReadInt16LittleEndian(data, offset + 0x1a),
                ReadAreaLinks(data, dataEnd, partOffsets, offset, index, 0),
                ReadAreaLinks(data, dataEnd, partOffsets, offset, index, 1),
                ReadAreaLinks(data, dataEnd, partOffsets, offset, index, 2),
                ReadAreaLinks(data, dataEnd, partOffsets, offset, index, 3),
                ReadAreaLinks(data, dataEnd, partOffsets, offset, index, 4));
        }

        return areas;
    }

    private static int[] ReadAreaLinks(
        ReadOnlySpan<byte> data,
        int dataEnd,
        int[] partOffsets,
        int areaOffset,
        int areaIndex,
        int part)
    {
        var count = ReadInt16LittleEndian(data, areaOffset + 0x10 + (part * 2));
        if (count < 0)
        {
            throw new InvalidDataException($"Gameplay area {areaIndex} part {part} count cannot be negative.");
        }
        if (count == 0)
        {
            return [];
        }

        var relativeOffset = ReadInt32LittleEndian(data, areaOffset + 0x1c + (part * 4));
        var offset = checked(4 + partOffsets[part] + relativeOffset);
        var byteLength = checked(count * 4);
        EnsureRange(data, offset, byteLength, $"gameplay area {areaIndex} part {part} links");
        if (offset + byteLength > dataEnd)
        {
            throw new InvalidDataException($"Gameplay area {areaIndex} part {part} links extend beyond area data.");
        }

        var links = new int[count];
        for (var index = 0; index < count; index++)
        {
            links[index] = ReadInt32LittleEndian(data, offset + (index * 4));
        }
        return links;
    }

    private static byte[] FindPayload(IReadOnlyList<GameplayRawBlock> blocks, string semanticName)
    {
        return blocks.FirstOrDefault(block => block.SemanticName == semanticName)?.PayloadBytes ?? [];
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> data, int offset, string context)
    {
        var value = ReadInt32LittleEndian(data, offset);
        if (value < 0)
        {
            throw new InvalidDataException($"{context} cannot be negative.");
        }
        return value;
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new float[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadSingleLittleEndian(data, offset + (index * 4));
        }
        return values;
    }

    private static GameplayVector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new GameplayVector3(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8));
    }

    private static GameplayVector4 ReadVector4(ReadOnlySpan<byte> data, int offset)
    {
        return new GameplayVector4(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8),
            ReadSingleLittleEndian(data, offset + 0x0c));
    }
}
