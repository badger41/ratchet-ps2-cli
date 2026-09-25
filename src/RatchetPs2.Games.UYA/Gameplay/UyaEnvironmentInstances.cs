using RatchetPs2.Core.Gameplay;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaCameraInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x20;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaCameraInstances? instances)
    {
        if (data.Length < HeaderSize)
        {
            instances = null;
            return false;
        }
        instances = Read(data);
        return true;
    }

    public static UyaCameraInstances Read(ReadOnlySpan<byte> data)
    {
        var table = UyaStaticInstanceReader.Read(data, RecordSize, "camera");
        return new(table.Count, table.HeaderWords, table.Records.Select(ReadInstance).ToArray(), table.TrailingBytes);
    }

    private static UyaCameraInstance ReadInstance(byte[] bytes) => new(
        ReadInt32LittleEndian(bytes, 0),
        ReadVector3(bytes, 4),
        ReadVector3(bytes, 0x10),
        ReadInt32LittleEndian(bytes, 0x1c),
        bytes);

    private static GameplayVector3 ReadVector3(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset), ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8));
}

public static class UyaSoundInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x90;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaSoundInstances? instances)
    {
        if (data.Length < HeaderSize)
        {
            instances = null;
            return false;
        }
        instances = Read(data);
        return true;
    }

    public static UyaSoundInstances Read(ReadOnlySpan<byte> data)
    {
        var table = UyaStaticInstanceReader.Read(data, RecordSize, "sound");
        return new(table.Count, table.HeaderWords, table.Records.Select(ReadInstance).ToArray(), table.TrailingBytes);
    }

    private static UyaSoundInstance ReadInstance(byte[] bytes) => new(
        ReadInt16LittleEndian(bytes, 0),
        ReadInt16LittleEndian(bytes, 2),
        ReadUInt32LittleEndian(bytes, 4),
        ReadInt32LittleEndian(bytes, 8),
        ReadSingleLittleEndian(bytes, 0xc),
        ReadFloats(bytes, 0x10, 16),
        ReadFloats(bytes, 0x50, 12),
        new(ReadSingleLittleEndian(bytes, 0x80), ReadSingleLittleEndian(bytes, 0x84),
            ReadSingleLittleEndian(bytes, 0x88)),
        ReadSingleLittleEndian(bytes, 0x8c),
        bytes);

    private static float[] ReadFloats(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new float[count];
        for (var index = 0; index < count; index++) values[index] = ReadSingleLittleEndian(data, offset + index * 4);
        return values;
    }
}

public sealed record UyaCameraInstances(
    int Count,
    IReadOnlyList<int> HeaderWords,
    IReadOnlyList<UyaCameraInstance> Instances,
    byte[] TrailingBytes);

public sealed record UyaCameraInstance(
    int Type,
    GameplayVector3 Position,
    GameplayVector3 Rotation,
    int PvarIndex,
    byte[] RawBytes);

public sealed record UyaSoundInstances(
    int Count,
    IReadOnlyList<int> HeaderWords,
    IReadOnlyList<UyaSoundInstance> Instances,
    byte[] TrailingBytes);

public sealed record UyaSoundInstance(
    short ClassId,
    short MissionClass,
    uint UpdateFunctionPointer,
    int PvarIndex,
    float Range,
    float[] Matrix,
    float[] InverseRotationMatrix,
    GameplayVector3 Rotation,
    float Padding,
    byte[] RawBytes);
