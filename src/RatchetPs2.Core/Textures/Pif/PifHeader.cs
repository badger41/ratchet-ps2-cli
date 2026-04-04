namespace RatchetPs2.Core.Textures.Pif;

public sealed record PifHeader(
    int Magic,
    int FileSize,
    int USize,
    int VSize,
    int TexFormat,
    int PaletteFormat,
    int PaletteOrder,
    int MipLevels)
{
    public const int SizeInBytes = 0x20;
    public const int ExpectedMagic = ('P' << 0x18) | ('I' << 0x10) | ('F' << 8) | '2';
    public const int SwizzledFlag = 0x80;

    public bool HasValidMagic => Magic == ExpectedMagic;

    public int BaseTextureFormat => TexFormat & 0x7f;

    public bool IsSwizzled => (TexFormat & SwizzledFlag) != 0;

    public int TextureFormatId => BaseTextureFormat;
}