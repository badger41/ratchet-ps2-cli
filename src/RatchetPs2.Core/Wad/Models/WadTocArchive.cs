namespace RatchetPs2.Core.Wad.Models;

public sealed record WadTocArchive(
    int TocSizeBytes,
    int TocIndex,
    long DataStartOffset,
    IReadOnlyList<WadTocEntry> Entries);