using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelConstants
{
    public const int SectorSize = Sector32.SizeInBytes;
    public const int TableOfContentsSector = 1500;
    public const int TableOfContentsSize = 0x2960;
    public const int LevelTableOffset = 0x28c8;
    public const int LevelCount = 19;
    public const int AmalgamatedHeaderSize = 0x2434;
    public const int LevelWadHeaderSize = 0x30;
    public const int LevelAudioWadHeaderSize = 0x164;
    public const int LevelSceneWadHeaderSize = 0x22b8;
    public const int LevelDataHeaderSize = 0x58;
}

public readonly record struct Rc1ByteRange(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;
}

public readonly record struct Rc1SectorByteRange(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;
}

public sealed record Rc1SceneHeader(
    IReadOnlyList<Sector32> Sounds,
    IReadOnlyList<Sector32> Wads);

public sealed record Rc1AmalgamatedLevelHeader(
    int Level,
    SectorRange Data,
    SectorRange GameplayNtsc,
    SectorRange GameplayPal,
    SectorRange Occlusion,
    IReadOnlyList<Rc1SectorByteRange> AudioData,
    IReadOnlyList<Sector32> Music,
    IReadOnlyList<Rc1SceneHeader> Scenes);

public sealed record Rc1LevelInfoEntry(
    int TableIndex,
    SectorRange TableRange,
    Rc1AmalgamatedLevelHeader Header);

public sealed record Rc1LevelWad(
    int HeaderSize,
    int Level,
    SectorRange Data,
    SectorRange GameplayNtsc,
    SectorRange GameplayPal,
    SectorRange Occlusion,
    byte[] HeaderBytes);

public sealed record Rc1LevelDataWad(
    Rc1ByteRange Overlay,
    Rc1ByteRange SoundBank,
    Rc1ByteRange CoreIndex,
    Rc1ByteRange GsRam,
    Rc1ByteRange HudHeader,
    IReadOnlyList<Rc1ByteRange> HudBanks,
    Rc1ByteRange CoreData,
    byte[] HeaderBytes);

public sealed record Rc1LooseLevelWad(
    int Level,
    int HeaderSector,
    int PayloadBaseSector,
    int SectorCount,
    Rc1LevelInfoEntry LevelInfo,
    Rc1LevelWad LevelWad,
    byte[] Bytes)
{
    public int ByteLength => Bytes.Length;
}

public sealed record Rc1ExtractedLevelWads(
    Rc1LooseLevelWad Level,
    byte[] Audio,
    byte[] Scene);

public sealed record Rc1LevelWadPackage(
    Rc1LevelWad LevelWad,
    IReadOnlyList<PackedFile> Files)
{
    public PackedFilePackage ToPackedPackage() => PackedFilePackageBuilder.Pack(Files);
}
