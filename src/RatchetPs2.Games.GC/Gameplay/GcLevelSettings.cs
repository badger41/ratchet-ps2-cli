using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.GC.Gameplay;

public static class GcLevelSettingsReader
{
    public const int MinimumSize = 0x28;

    public static bool TryRead(ReadOnlySpan<byte> data, out GcLevelSettings? settings)
    {
        if (data.Length < MinimumSize)
        {
            settings = null;
            return false;
        }

        settings = new GcLevelSettings(
            ReadRgb(data, 0x00),
            ReadRgb(data, 0x0c),
            ReadSingleLittleEndian(data, 0x18),
            ReadSingleLittleEndian(data, 0x1c),
            ReadSingleLittleEndian(data, 0x20),
            ReadSingleLittleEndian(data, 0x24),
            data.Length - MinimumSize);
        return true;
    }

    public static GcLevelSettings Read(ReadOnlySpan<byte> data)
    {
        if (!TryRead(data, out var settings))
        {
            throw new InvalidDataException($"GC level settings are too small to contain the 0x{MinimumSize:X}-byte header.");
        }

        return settings!;
    }

    private static GcRgb96 ReadRgb(ReadOnlySpan<byte> data, int offset)
    {
        return new GcRgb96(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8));
    }
}

public sealed record GcLevelSettings(
    GcRgb96 BackgroundColor,
    GcRgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity,
    int TrailingByteLength);

public readonly record struct GcRgb96(int Red, int Green, int Blue);
