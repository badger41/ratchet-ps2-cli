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
