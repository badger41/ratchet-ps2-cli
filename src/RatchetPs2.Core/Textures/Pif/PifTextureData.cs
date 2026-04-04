namespace RatchetPs2.Core.Textures.Pif;

public sealed class PifTextureData
{
    public PifTextureData(
        PifHeader header,
        PifTextureEncoding encoding,
        byte[] paletteData,
        byte[] pixelData)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Encoding = encoding;
        PaletteData = paletteData ?? throw new ArgumentNullException(nameof(paletteData));
        PixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
    }

    public PifHeader Header { get; }

    public PifTextureEncoding Encoding { get; }

    public bool IsSwizzled => Header.IsSwizzled;

    public byte[] PaletteData { get; }

    public byte[] PixelData { get; }
}