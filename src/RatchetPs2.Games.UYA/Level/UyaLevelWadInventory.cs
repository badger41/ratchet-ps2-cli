namespace RatchetPs2.Games.UYA.Level;

public enum UyaContainerCompression
{
    None,
    Wad,
}

public enum UyaContainerRegionKind
{
    Header,
    Payload,
    Padding,
    Opaque,
}

public sealed record UyaContainerSlot(
    string Path,
    IReadOnlyList<string> LogicalPaths,
    int DeclaredOffset,
    int DeclaredLength,
    int Offset,
    int Length,
    int Alignment,
    UyaContainerCompression Compression,
    string Sha256,
    ReadOnlyMemory<byte> Bytes);

public sealed record UyaContainerRegion(
    string Path,
    int Offset,
    int Length,
    UyaContainerRegionKind Kind,
    string? SlotPath,
    ReadOnlyMemory<byte> Bytes);

public sealed record UyaContainerInventory(
    string Path,
    string? SourceContainerPath,
    int SourceOffset,
    int SourceLength,
    UyaContainerCompression SourceCompression,
    string Sha256,
    ReadOnlyMemory<byte> Bytes,
    IReadOnlyList<UyaContainerSlot> Slots,
    IReadOnlyList<UyaContainerRegion> Regions);

public sealed record UyaLevelWadInventory(
    UyaLevelWad LevelWad,
    IReadOnlyList<UyaContainerInventory> Containers);
