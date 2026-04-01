namespace RatchetPs2.Core.IO;

public readonly record struct SectorRange(Sector32 Offset, Sector32 Size)
{
    public long OffsetBytes => Offset.ToByteOffset();
    public long SizeBytes => Size.ToByteOffset();
    public bool IsEmpty => Size.Value <= 0;
}