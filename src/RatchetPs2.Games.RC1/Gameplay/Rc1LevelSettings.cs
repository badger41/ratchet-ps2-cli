using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.RC1.Gameplay;

public static class Rc1LevelSettingsReader
{
    public const int Size = 0x50;

    public static bool TryRead(ReadOnlySpan<byte> data, out Rc1LevelSettings? settings)
    {
        if (data.Length < Size)
        {
            settings = null;
            return false;
        }

        settings = new Rc1LevelSettings(
            ReadRgb(data, 0x00),
            ReadRgb(data, 0x0c),
            ReadSingleLittleEndian(data, 0x18),
            ReadSingleLittleEndian(data, 0x1c),
            ReadSingleLittleEndian(data, 0x20),
            ReadSingleLittleEndian(data, 0x24),
            ReadSingleLittleEndian(data, 0x28),
            ReadVector3(data, 0x2c),
            ReadSingleLittleEndian(data, 0x38),
            ReadInt32LittleEndian(data, 0x3c),
            ReadInt32LittleEndian(data, 0x40),
            ReadInt32LittleEndian(data, 0x44));
        return true;
    }

    public static Rc1LevelSettings Read(ReadOnlySpan<byte> data) =>
        TryRead(data, out var settings)
            ? settings!
            : throw new InvalidDataException($"RC1 level settings are smaller than 0x{Size:X} bytes.");

    private static Rc1Rgb96 ReadRgb(ReadOnlySpan<byte> data, int offset) => new(
        ReadInt32LittleEndian(data, offset),
        ReadInt32LittleEndian(data, offset + 4),
        ReadInt32LittleEndian(data, offset + 8));

    private static Rc1Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset),
        ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8));
}

public sealed record Rc1LevelSettings(
    Rc1Rgb96 BackgroundColor,
    Rc1Rgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity,
    float DeathHeight,
    Rc1Vector3 ShipPosition,
    float ShipRotationZ,
    int ShipPath,
    int ShipCameraCuboidStart,
    int ShipCameraCuboidEnd);
