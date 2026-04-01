namespace RatchetPs2.Core.Wad.Models;

public sealed record WadArchive(int HeaderSize, IReadOnlyList<WadEntry> Entries);