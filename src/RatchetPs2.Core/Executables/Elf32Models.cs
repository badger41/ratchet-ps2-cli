namespace RatchetPs2.Core.Executables;

public sealed record Elf32Section(
    string Name,
    uint Type,
    uint Flags,
    uint Address,
    uint Alignment,
    uint EntrySize,
    int Segment,
    byte[] Bytes);

public sealed record Elf32Image(uint EntryPoint, uint Flags, IReadOnlyList<Elf32Section> Sections);
