using System.Buffers.Binary;
using RatchetPs2.Core.Gameplay;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.RC1.Gameplay;

public static class Rc1Gameplay
{
    public const int CoreHeaderSize = 0x94;
    public const int TieInstanceSize = 0xe0;
    public const int PortableTieInstanceSize = 0x60;

    public static GameplayLayout CoreLayout { get; } = new(
        "RC1",
        "core",
        CoreHeaderSize,
        [
            new(0x00, "level_settings"),
            new(0x04, "directional_lights"),
            new(0x08, "cameras"),
            new(0x0c, "sound_instances"),
            new(0x10, "us_english_strings"),
            new(0x14, "uk_english_strings"),
            new(0x18, "french_strings"),
            new(0x1c, "german_strings"),
            new(0x20, "spanish_strings"),
            new(0x24, "italian_strings"),
            new(0x28, "japanese_strings"),
            new(0x2c, "korean_strings"),
            new(0x30, "tie_classes"),
            new(0x34, "tie_instances"),
            new(0x38, "shrub_classes"),
            new(0x3c, "shrub_instances"),
            new(0x40, "moby_classes"),
            new(0x44, "moby_instances"),
            new(0x48, "moby_groups"),
            new(0x4c, "shared_data"),
            new(0x50, "pvar_moby_links"),
            new(0x54, "pvar_table"),
            new(0x58, "pvar_data"),
            new(0x5c, "pvar_relative_pointers"),
            new(0x60, "cuboids"),
            new(0x64, "spheres"),
            new(0x68, "cylinders"),
            new(0x6c, "pills"),
            new(0x70, "splines"),
            new(0x74, "grind_splines"),
            new(0x78, "point_light_grid"),
            new(0x7c, "point_lights"),
            new(0x80, "env_transitions"),
            new(0x84, "camera_collision_grid"),
            new(0x88, "env_sample_points"),
            new(0x8c, "occlusion"),
            new(0x90, "padding")
        ]);

    public static GameplayRawBlocks ReadCore(ReadOnlySpan<byte> data) =>
        GameplayLayoutReader.Read(data, CoreLayout);

    public static (byte[] Instances, byte[] AmbientRgbas) ConvertTieInstances(ReadOnlySpan<byte> data)
    {
        const int tableHeaderSize = 0x10;
        if (data.Length < tableHeaderSize)
        {
            throw new InvalidDataException("RC1 tie instance data is too small to contain its table header.");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        var sourceLength = checked(tableHeaderSize + Math.Max(count, 0) * TieInstanceSize);
        if (count < 0 || sourceLength > data.Length)
        {
            throw new InvalidDataException("RC1 tie instance count exceeds the available gameplay data.");
        }

        var instances = new byte[checked(tableHeaderSize + count * PortableTieInstanceSize)];
        data[..tableHeaderSize].CopyTo(instances);
        var ambientRgbas = new byte[checked(count * (sizeof(short) * 2 + 0x80))];

        for (var index = 0; index < count; index++)
        {
            var source = data.Slice(tableHeaderSize + index * TieInstanceSize, TieInstanceSize);
            var destination = instances.AsSpan(tableHeaderSize + index * PortableTieInstanceSize, PortableTieInstanceSize);
            source[..0x50].CopyTo(destination);
            source.Slice(0xd0, 0x10).CopyTo(destination[0x50..]);

            var ambient = ambientRgbas.AsSpan(index * 0x84, 0x84);
            BinaryPrimitives.WriteInt16LittleEndian(ambient, checked((short)index));
            BinaryPrimitives.WriteInt16LittleEndian(ambient[2..], 0x40);
            source.Slice(0x50, 0x80).CopyTo(ambient[4..]);
        }

        return (instances, ambientRgbas);
    }
}

public static class Rc1MobyInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x78;

    public static bool TryRead(ReadOnlySpan<byte> data, out Rc1MobyInstances? mobyInstances)
    {
        if (data.Length < HeaderSize)
        {
            mobyInstances = null;
            return false;
        }

        var count = ReadInt32LittleEndian(data, 0);
        if (count < 0 || (long)count * RecordSize > data.Length - HeaderSize)
        {
            mobyInstances = null;
            return false;
        }

        mobyInstances = Read(data);
        return true;
    }

    public static Rc1MobyInstances Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "RC1 moby instances header");
        var staticCount = ReadInt32LittleEndian(data, 0);
        if (staticCount < 0)
        {
            throw new InvalidDataException("RC1 moby instances static count cannot be negative.");
        }

        var recordsLength = checked(staticCount * RecordSize);
        EnsureRange(data, HeaderSize, recordsLength, "RC1 moby instance records");
        var instances = new Rc1MobyInstance[staticCount];
        for (var index = 0; index < staticCount; index++)
        {
            instances[index] = ReadInstance(data, HeaderSize + index * RecordSize);
        }

        return new Rc1MobyInstances(
            staticCount,
            ReadInt32LittleEndian(data, 4),
            ReadInt32LittleEndian(data, 8),
            ReadInt32LittleEndian(data, 0x0c),
            instances,
            data[(HeaderSize + recordsLength)..].ToArray());
    }

    private static Rc1MobyInstance ReadInstance(ReadOnlySpan<byte> data, int offset) => new(
        ReadInt32LittleEndian(data, offset),
        ReadInt32LittleEndian(data, offset + 4),
        ReadInt32LittleEndian(data, offset + 8),
        ReadInt32LittleEndian(data, offset + 0x0c),
        ReadInt32LittleEndian(data, offset + 0x10),
        ReadInt32LittleEndian(data, offset + 0x14),
        ReadInt32LittleEndian(data, offset + 0x18),
        ReadSingleLittleEndian(data, offset + 0x1c),
        ReadSingleLittleEndian(data, offset + 0x20),
        ReadInt32LittleEndian(data, offset + 0x24),
        ReadInt32LittleEndian(data, offset + 0x28),
        ReadInt32LittleEndian(data, offset + 0x2c),
        ReadVector3(data, offset + 0x30),
        ReadVector3(data, offset + 0x3c),
        ReadInt32LittleEndian(data, offset + 0x48),
        ReadInt32LittleEndian(data, offset + 0x4c),
        ReadSingleLittleEndian(data, offset + 0x50),
        ReadInt32LittleEndian(data, offset + 0x54),
        ReadInt32LittleEndian(data, offset + 0x58),
        ReadInt32LittleEndian(data, offset + 0x5c),
        ReadInt32LittleEndian(data, offset + 0x60),
        ReadRgb(data, offset + 0x64),
        ReadInt32LittleEndian(data, offset + 0x70),
        ReadInt32LittleEndian(data, offset + 0x74));

    private static Rc1Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset),
        ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8));

    private static Rc1Rgb96 ReadRgb(ReadOnlySpan<byte> data, int offset) => new(
        ReadInt32LittleEndian(data, offset),
        ReadInt32LittleEndian(data, offset + 4),
        ReadInt32LittleEndian(data, offset + 8));
}

public sealed record Rc1MobyInstances(
    int StaticCount,
    int SpawnableMobyCount,
    int Pad8,
    int PadC,
    IReadOnlyList<Rc1MobyInstance> Instances,
    byte[] TrailingBytes);

public sealed record Rc1MobyInstance(
    int Size,
    int Unknown4,
    int Unknown8,
    int UnknownC,
    int Unknown10,
    int Unknown14,
    int ClassId,
    float Scale,
    float DrawDistance,
    int UpdateDistance,
    int Unused28,
    int Unused2C,
    Rc1Vector3 Position,
    Rc1Vector3 Rotation,
    int Group,
    int IsRooted,
    float RootedDistance,
    int Unknown54,
    int PvarIndex,
    int Occlusion,
    int ModeBits,
    Rc1Rgb96 Color,
    int Light,
    int Unknown74);

public readonly record struct Rc1Vector3(float X, float Y, float Z);
public readonly record struct Rc1Rgb96(int Red, int Green, int Blue);
