using System.Buffers.Binary;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaLevelSettingsReader
{
    public const int MinimumSize = 0x84;

    private const int FirstPartSize = 0x5c;
    private const int ChunkPlaneSize = 0x20;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaLevelSettings? settings)
    {
        if (data.Length < MinimumSize)
        {
            settings = null;
            return false;
        }

        settings = Read(data);
        return true;
    }

    public static UyaLevelSettings Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, MinimumSize, "UYA level settings");

        var chunkPlaneCount = ReadInt32LittleEndian(data, FirstPartSize + 0x0c);
        var chunkPlaneCursor = FirstPartSize;
        var chunkPlanes = new List<UyaLevelSettingsChunkPlane>();
        if (chunkPlaneCount > 0)
        {
            // Explicit overflow validation also works with C# to JavaScript compilers.
            if (chunkPlaneCount > int.MaxValue / ChunkPlaneSize)
                throw new OverflowException();
            EnsureRange(data, chunkPlaneCursor, chunkPlaneCount * ChunkPlaneSize, "UYA level settings chunk planes");
            for (var i = 0; i < chunkPlaneCount; i++)
            {
                chunkPlanes.Add(ReadChunkPlane(data, chunkPlaneCursor));
                chunkPlaneCursor += ChunkPlaneSize;
            }
        }
        else
        {
            chunkPlaneCursor += ChunkPlaneSize;
        }

        var coreSoundsCount = ReadInt32LittleEndian(data, chunkPlaneCursor);
        var rac3ThirdPartOffset = chunkPlaneCursor + sizeof(int);
        var hasRac3ThirdPart = data.Length >= rac3ThirdPartOffset + sizeof(int);
        var rac3ThirdPart = hasRac3ThirdPart ? ReadInt32LittleEndian(data, rac3ThirdPartOffset) : 0;
        var trailingOffset = hasRac3ThirdPart ? rac3ThirdPartOffset + sizeof(int) : data.Length;

        return new UyaLevelSettings(
            ReadRgb(data, 0x00),
            ReadRgb(data, 0x0c),
            ReadSingleLittleEndian(data, 0x18),
            ReadSingleLittleEndian(data, 0x1c),
            ReadSingleLittleEndian(data, 0x20),
            ReadSingleLittleEndian(data, 0x24),
            ReadSingleLittleEndian(data, 0x28),
            ReadInt32LittleEndian(data, 0x2c) != 0,
            ReadVector3(data, 0x30),
            ReadVector3(data, 0x3c),
            ReadSingleLittleEndian(data, 0x48),
            ReadInt32LittleEndian(data, 0x4c),
            ReadInt32LittleEndian(data, 0x50),
            ReadInt32LittleEndian(data, 0x54),
            ReadUInt32LittleEndian(data, 0x58),
            chunkPlanes,
            coreSoundsCount,
            rac3ThirdPart,
            SliceToArray(data, trailingOffset, data.Length - trailingOffset, "UYA level settings trailing bytes"));
    }

    private static UyaRgb96 ReadRgb(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaRgb96(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8));
    }

    private static UyaVector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaVector3(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8));
    }

    private static UyaLevelSettingsChunkPlane ReadChunkPlane(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaLevelSettingsChunkPlane(
            ReadVector3(data, offset),
            ReadInt32LittleEndian(data, offset + 0x0c),
            ReadVector3(data, offset + 0x10),
            ReadUInt32LittleEndian(data, offset + 0x1c));
    }
}

public sealed record UyaLevelSettings(
    UyaRgb96 BackgroundColor,
    UyaRgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity,
    float DeathHeight,
    bool IsSphericalWorld,
    UyaVector3 SphereCenter,
    UyaVector3 ShipPosition,
    float ShipRotationZ,
    int ShipPath,
    int ShipCameraCuboidStart,
    int ShipCameraCuboidEnd,
    uint Padding58,
    IReadOnlyList<UyaLevelSettingsChunkPlane> ChunkPlanes,
    int CoreSoundsCount,
    int Rac3ThirdPart,
    byte[] TrailingBytes);

public readonly record struct UyaRgb96(int Red, int Green, int Blue);

public readonly record struct UyaVector3(float X, float Y, float Z);

public readonly record struct UyaLevelSettingsChunkPlane(
    UyaVector3 Point,
    int PlaneCount,
    UyaVector3 Normal,
    uint Padding);

public sealed record UyaLevelSettingsEdit(
    UyaRgb96 BackgroundColor,
    UyaRgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity);

public static class UyaLevelSettingsWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, UyaLevelSettingsEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        _ = UyaLevelSettingsReader.Read(source);
        ValidateColor(edit.BackgroundColor, "background");
        ValidateColor(edit.FogColor, "fog");
        var values = new[]
        {
            edit.FogNearDistance, edit.FogFarDistance, edit.FogNearIntensity, edit.FogFarIntensity,
        };
        if (values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("UYA level settings fog values must be finite.");
        if (edit.FogNearDistance < 0 || edit.FogFarDistance < 0)
            throw new InvalidDataException("UYA level settings fog distances cannot be negative.");
        if (edit.FogNearIntensity is < 0 or > 255 || edit.FogFarIntensity is < 0 or > 255)
            throw new InvalidDataException("UYA level settings fog intensities must be between 0 and 255.");

        var output = source.ToArray();
        WriteColor(output, 0x00, edit.BackgroundColor);
        WriteColor(output, 0x0c, edit.FogColor);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(0x18), edit.FogNearDistance);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(0x1c), edit.FogFarDistance);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(0x20), edit.FogNearIntensity);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(0x24), edit.FogFarIntensity);
        return output;
    }

    private static void ValidateColor(UyaRgb96 value, string name)
    {
        if (value.Red is < 0 or > 255 || value.Green is < 0 or > 255 || value.Blue is < 0 or > 255)
            throw new InvalidDataException($"UYA level settings {name} color channels must be between 0 and 255.");
    }

    private static void WriteColor(Span<byte> output, int offset, UyaRgb96 value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output[offset..], value.Red);
        BinaryPrimitives.WriteInt32LittleEndian(output[(offset + 4)..], value.Green);
        BinaryPrimitives.WriteInt32LittleEndian(output[(offset + 8)..], value.Blue);
    }
}
