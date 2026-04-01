using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Wad.Models;

public sealed record WadEntry(int Index, SectorRange Range)
{
    public long OffsetBytes => Range.OffsetBytes;
    public long SizeBytes => Range.SizeBytes;
}