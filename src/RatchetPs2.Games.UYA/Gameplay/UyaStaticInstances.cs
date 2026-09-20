using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaTieInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x60;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaTieInstances? instances)
    {
        if (data.Length < HeaderSize)
        {
            instances = null;
            return false;
        }
        instances = Read(data);
        return true;
    }

    public static UyaTieInstances Read(ReadOnlySpan<byte> data)
    {
        var table = UyaStaticInstanceReader.Read(data, RecordSize, "tie");
        return new(table.Count, table.HeaderWords, table.Records.Select(ReadInstance).ToArray(), table.TrailingBytes);
    }

    private static UyaTieInstance ReadInstance(byte[] bytes) => new(
        ReadInt32LittleEndian(bytes, 0),
        UyaStaticInstanceReader.ReadTransform(bytes),
        bytes);
}

public static class UyaShrubInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x70;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaShrubInstances? instances)
    {
        if (data.Length < HeaderSize)
        {
            instances = null;
            return false;
        }
        instances = Read(data);
        return true;
    }

    public static UyaShrubInstances Read(ReadOnlySpan<byte> data)
    {
        var table = UyaStaticInstanceReader.Read(data, RecordSize, "shrub");
        return new(table.Count, table.HeaderWords, table.Records.Select(ReadInstance).ToArray(), table.TrailingBytes);
    }

    private static UyaShrubInstance ReadInstance(byte[] bytes) => new(
        ReadInt32LittleEndian(bytes, 0),
        ReadSingleLittleEndian(bytes, 4),
        UyaStaticInstanceReader.ReadTransform(bytes),
        bytes);
}

internal static class UyaStaticInstanceReader
{
    public static UyaStaticInstanceTable Read(ReadOnlySpan<byte> data, int recordSize, string family)
    {
        EnsureRange(data, 0, UyaTieInstancesReader.HeaderSize, $"UYA {family} instances header");
        var count = ReadInt32LittleEndian(data, 0);
        if (count < 0) throw new InvalidDataException($"UYA {family} instance count cannot be negative.");
        var recordsLength = checked(count * recordSize);
        EnsureRange(data, UyaTieInstancesReader.HeaderSize, recordsLength, $"UYA {family} instance records");
        var records = new byte[count][];
        for (var index = 0; index < count; index++)
            records[index] = data.Slice(UyaTieInstancesReader.HeaderSize + index * recordSize, recordSize).ToArray();
        return new(
            count,
            [ReadInt32LittleEndian(data, 4), ReadInt32LittleEndian(data, 8), ReadInt32LittleEndian(data, 12)],
            records,
            data[(UyaTieInstancesReader.HeaderSize + recordsLength)..].ToArray());
    }

    public static UyaInstanceTransform ReadTransform(ReadOnlySpan<byte> data) => new(
        ReadVector4(data, 0x10),
        ReadVector4(data, 0x20),
        ReadVector4(data, 0x30),
        ReadVector4(data, 0x40));

    private static UyaVector4 ReadVector4(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset),
        ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8),
        ReadSingleLittleEndian(data, offset + 12));
}

internal sealed record UyaStaticInstanceTable(
    int Count,
    IReadOnlyList<int> HeaderWords,
    IReadOnlyList<byte[]> Records,
    byte[] TrailingBytes);

public sealed record UyaTieInstances(
    int Count,
    IReadOnlyList<int> HeaderWords,
    IReadOnlyList<UyaTieInstance> Instances,
    byte[] TrailingBytes);

public sealed record UyaShrubInstances(
    int Count,
    IReadOnlyList<int> HeaderWords,
    IReadOnlyList<UyaShrubInstance> Instances,
    byte[] TrailingBytes);

public sealed record UyaTieInstance(int ClassId, UyaInstanceTransform Transform, byte[] RawBytes);

public sealed record UyaShrubInstance(int ClassId, float DrawDistance, UyaInstanceTransform Transform, byte[] RawBytes);

public sealed record UyaInstanceTransform(
    UyaVector4 BasisX,
    UyaVector4 BasisY,
    UyaVector4 BasisZ,
    UyaVector4 Position);

public readonly record struct UyaVector4(float X, float Y, float Z, float W);
