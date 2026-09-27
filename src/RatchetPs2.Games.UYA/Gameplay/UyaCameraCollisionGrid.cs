using System.Buffers.Binary;
using System.Numerics;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaCameraCollisionGridReader
{
    private const int GridSize = 64 * 64;
    private const int GridOffset = 0x10;
    private const int PrimitiveSize = 0x30;

    public static UyaCameraCollisionGrid Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, GridOffset, GridSize * sizeof(int), "UYA camera collision grid");
        var primitives = new Dictionary<(int Type, int Index), UyaCameraCollisionPrimitive>();
        var occupiedCells = 0;
        for (var cell = 0; cell < GridSize; cell++)
        {
            var relativeOffset = ReadInt32LittleEndian(data, GridOffset + cell * sizeof(int));
            if (relativeOffset == 0) continue;
            occupiedCells++;
            if (relativeOffset < GridSize * sizeof(int))
                throw new InvalidDataException("UYA camera collision cell points inside the grid table.");
            var listOffset = checked(GridOffset + relativeOffset);
            EnsureRange(data, listOffset, 0x10, "UYA camera collision cell header");
            var count = ReadInt32LittleEndian(data, listOffset);
            if (count < 0) throw new InvalidDataException("UYA camera collision primitive count is negative.");
            EnsureRange(data, listOffset + 0x10, checked(count * PrimitiveSize), "UYA camera collision primitives");
            for (var index = 0; index < count; index++)
            {
                var offset = listOffset + 0x10 + index * PrimitiveSize;
                var primitive = new UyaCameraCollisionPrimitive(
                    ReadVector4(data, offset),
                    ReadInt32LittleEndian(data, offset + 0x10),
                    ReadInt32LittleEndian(data, offset + 0x14),
                    ReadInt32LittleEndian(data, offset + 0x18),
                    ReadInt32LittleEndian(data, offset + 0x1c),
                    ReadSingleLittleEndian(data, offset + 0x20));
                if (primitive.Type is not (3 or 5 or 6 or 7))
                    throw new InvalidDataException($"UYA camera collision primitive type {primitive.Type} is invalid.");
                if (primitive.Index < 0)
                    throw new InvalidDataException("UYA camera collision primitive index is negative.");
                var key = (primitive.Type, primitive.Index);
                if (primitives.TryGetValue(key, out var previous) && previous != primitive)
                    throw new InvalidDataException("UYA camera collision primitive metadata is inconsistent across grid cells.");
                primitives[key] = primitive;
            }
        }
        return new(occupiedCells, primitives.Values.OrderBy(value => value.Type).ThenBy(value => value.Index).ToArray());
    }

    private static UyaVector4 ReadVector4(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset),
        ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8),
        ReadSingleLittleEndian(data, offset + 12));
}

public sealed record UyaCameraCollisionGrid(
    int OccupiedCellCount,
    IReadOnlyList<UyaCameraCollisionPrimitive> Primitives);

public sealed record UyaCameraCollisionPrimitive(
    UyaVector4 BoundingSphere,
    int Type,
    int Index,
    int Flags,
    int IntValue,
    float FloatValue);

public sealed record UyaCameraCollisionPrimitiveEdit(
    int Type,
    int Index,
    int Flags,
    int IntValue,
    float FloatValue,
    UyaVector3 Position,
    UyaQuaternion Rotation,
    UyaVector3 Scale);

public static class UyaCameraCollisionGridWriter
{
    private const int GridWidth = 64;
    private const int GridOffset = 0x10;
    private const int PrimitiveSize = 0x30;

    public static byte[] Write(IReadOnlyList<UyaCameraCollisionPrimitiveEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var cells = Enumerable.Range(0, GridWidth * GridWidth)
            .Select(_ => new List<(UyaCameraCollisionPrimitiveEdit Edit, UyaVector4 Bounds)>()).ToArray();
        var seen = new HashSet<(int Type, int Index)>();
        foreach (var edit in edits.OrderBy(value => value.Type).ThenBy(value => value.Index))
        {
            if (edit.Type is not (3 or 5 or 6 or 7) || edit.Index < 0 || !seen.Add((edit.Type, edit.Index)))
                throw new InvalidDataException("UYA camera-collision primitive identity is invalid or duplicated.");
            if (!float.IsFinite(edit.FloatValue))
                throw new InvalidDataException("UYA camera-collision primitive value must be finite.");
            var matrix = UyaInstanceTransformWriter.CreateMatrix(
                new(edit.Index, edit.Position, edit.Rotation, edit.Scale), "camera collision");
            var corners = new List<Vector3>(8);
            for (var z = -1; z <= 1; z += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var x = -1; x <= 1; x += 2)
                corners.Add(Vector3.Transform(new(x, y, z), matrix));
            var min = new Vector3(corners.Min(value => value.X), corners.Min(value => value.Y),
                corners.Min(value => value.Z));
            var max = new Vector3(corners.Max(value => value.X), corners.Max(value => value.Y),
                corners.Max(value => value.Z));
            var center = (min + max) / 2;
            var bounds = new UyaVector4(center.X, center.Y, 0,
                corners.Max(value => Vector3.Distance(center, value)));
            var xmin = Math.Clamp((int)(min.X / 16), 0, GridWidth);
            var ymin = Math.Clamp((int)(min.Y / 16), 0, GridWidth);
            var xmax = Math.Clamp((int)MathF.Ceiling(max.X / 16), 0, GridWidth);
            var ymax = Math.Clamp((int)MathF.Ceiling(max.Y / 16), 0, GridWidth);
            for (var y = ymin; y < ymax; y++)
            for (var x = xmin; x < xmax; x++)
                cells[y * GridWidth + x].Add((edit, bounds));
        }

        var size = checked(GridOffset + cells.Length * sizeof(int)
            + cells.Where(value => value.Count > 0).Sum(value => 0x10 + value.Count * PrimitiveSize));
        var output = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(output, size - sizeof(int));
        var offset = GridOffset + cells.Length * sizeof(int);
        for (var cell = 0; cell < cells.Length; cell++)
        {
            if (cells[cell].Count == 0) continue;
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(GridOffset + cell * sizeof(int)), offset - GridOffset);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), cells[cell].Count);
            offset += 0x10;
            foreach (var (edit, bounds) in cells[cell])
            {
                UyaInstanceTransformWriter.WriteVector4(output.AsSpan(offset, PrimitiveSize), 0,
                    bounds.X, bounds.Y, bounds.Z, bounds.W);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x10), edit.Type);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x14), edit.Index);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x18), edit.Flags);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x1c), edit.IntValue);
                BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(offset + 0x20), edit.FloatValue);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x24), -1);
                offset += PrimitiveSize;
            }
        }
        return output;
    }
}
