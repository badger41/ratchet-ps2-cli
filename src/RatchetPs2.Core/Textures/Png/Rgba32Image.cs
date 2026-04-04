namespace RatchetPs2.Core.Textures.Png;

public sealed class Rgba32Image
{
    public Rgba32Image(int width, int height, byte[] pixelData)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(pixelData);

        var expectedLength = checked(width * height * 4);
        if (pixelData.Length != expectedLength)
        {
            throw new ArgumentException(
                $"RGBA pixel data length must be exactly {expectedLength} bytes for a {width}x{height} image.",
                nameof(pixelData));
        }

        Width = width;
        Height = height;
        PixelData = pixelData;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] PixelData { get; }
}