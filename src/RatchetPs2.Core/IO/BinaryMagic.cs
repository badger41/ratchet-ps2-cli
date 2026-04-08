using RatchetPs2.Core.Textures.Pif;
using System.Buffers.Binary;

namespace RatchetPs2.Core.IO;

public enum BinaryDataKind
{
    Unknown,
    Pif,
    Wad,
}

public static class BinaryMagic
{
    private static ReadOnlySpan<byte> WadMagic => "WAD"u8;

    public static BinaryDataKind Detect(ReadOnlySpan<byte> data)
    {
        if (IsPif(data))
        {
            return BinaryDataKind.Pif;
        }

        if (IsWad(data))
        {
            return BinaryDataKind.Wad;
        }

        return BinaryDataKind.Unknown;
    }

    public static bool IsPif(ReadOnlySpan<byte> data)
    {
        return data.Length >= sizeof(int)
            && BinaryPrimitives.ReadInt32LittleEndian(data[..sizeof(int)]) == PifHeader.ExpectedMagic;
    }

    public static bool IsWad(ReadOnlySpan<byte> data)
    {
        return data.Length >= WadMagic.Length && data[..WadMagic.Length].SequenceEqual(WadMagic);
    }
}