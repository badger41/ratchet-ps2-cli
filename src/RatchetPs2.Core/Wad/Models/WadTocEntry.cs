using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Wad.Models;

public sealed record WadTocEntry(int Index, SectorRange Range, long? ByteLength = null)
{
    public long OffsetBytes => Range.OffsetBytes;

    public long SizeBytes => ByteLength ?? Range.SizeBytes;
}