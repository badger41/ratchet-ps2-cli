namespace RatchetPs2.Core.IO;

public readonly record struct Sector32(int Value)
{
    public const int SizeInBytes = 0x800;

    public long ToByteOffset() => (long)Value * SizeInBytes;
}