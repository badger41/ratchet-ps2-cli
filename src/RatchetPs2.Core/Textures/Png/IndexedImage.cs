namespace RatchetPs2.Core.Textures.Png;

public sealed class IndexedImage
{
    public IndexedImage(int width, int height, int bitsPerPixel, byte[] paletteRgba, byte[] pixelIndices)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (bitsPerPixel is not 4 and not 8)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerPixel));
        }

        ArgumentNullException.ThrowIfNull(paletteRgba);
        ArgumentNullException.ThrowIfNull(pixelIndices);

        if (paletteRgba.Length % 4 != 0)
        {
            throw new ArgumentException("Palette data length must be a multiple of 4 bytes.", nameof(paletteRgba));
        }

        var expectedPixelCount = checked(width * height);
        if (pixelIndices.Length != expectedPixelCount)
        {
            throw new ArgumentException(
                $"Indexed pixel data length must be exactly {expectedPixelCount} bytes for a {width}x{height} image.",
                nameof(pixelIndices));
        }

        var paletteEntryCount = paletteRgba.Length / 4;
        var maxEntries = bitsPerPixel == 4 ? 16 : 256;
        if (paletteEntryCount > maxEntries)
        {
            throw new ArgumentException(
                $"Palette contains {paletteEntryCount} entries, which exceeds the {maxEntries} entries supported by {bitsPerPixel}bpp indexed PNGs.",
                nameof(paletteRgba));
        }

        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        PaletteRgba = paletteRgba;
        PixelIndices = pixelIndices;
    }

    public int Width { get; }

    public int Height { get; }

    public int BitsPerPixel { get; }

    public byte[] PaletteRgba { get; }

    public byte[] PixelIndices { get; }
}