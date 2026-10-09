using System.Buffers.Binary;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Textures;

public sealed record Indexed8PifImport(int Width, int Height, byte[] PifBytes);

public static class Indexed8PifImporter
{
    public const int MaximumImageBytes = 16 * 1024 * 1024;

    public static Indexed8PifImport Convert(
        string format,
        byte[] bytes,
        int maximumDimension,
        string family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length is 0 or > MaximumImageBytes)
            throw new InvalidDataException($"{family} image must contain at most {MaximumImageBytes} bytes.");
        return format.Trim().ToLowerInvariant() switch
        {
            "png" => ConvertPng(bytes, maximumDimension, family, cancellationToken),
            "pif" => NormalizePif(bytes, maximumDimension, family),
            _ => throw new InvalidDataException($"{family} images must use PNG or PIF format."),
        };
    }

    private static Indexed8PifImport ConvertPng(
        byte[] bytes,
        int maximumDimension,
        string family,
        CancellationToken cancellationToken)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)
            [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            throw new InvalidDataException($"{family} image is not a PNG file.");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        ValidateDimensions(width, height, maximumDimension, family);
        using var stream = new MemoryStream(bytes, writable: false);
        var image = PngTextureMetadataReader.ReadRgba32(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var pixels = new TextureColor[checked(image.Width * image.Height)];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            pixels[index] = new(
                image.PixelData[offset], image.PixelData[offset + 1], image.PixelData[offset + 2],
                image.PixelData[offset + 3]);
        }
        var quantized = AlphaAwareKMeans.Quantize(pixels, 256, cancellationToken);
        var palette = new byte[0x400];
        for (var index = 0; index < quantized.Palette.Count; index++)
        {
            var color = quantized.Palette[index];
            var offset = index * 4;
            palette[offset] = color.Red;
            palette[offset + 1] = color.Green;
            palette[offset + 2] = color.Blue;
            palette[offset + 3] = (byte)Math.Min(128, (color.Alpha + 1) / 2);
        }
        var indexes = quantized.Indices
            .Select(value => TextureConverter.DecodePaletteIndex(checked((byte)value)))
            .ToArray();
        return Canonical(image.Width, image.Height, palette, indexes);
    }

    private static Indexed8PifImport NormalizePif(byte[] bytes, int maximumDimension, string family)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var header = PifReader.ReadHeader(stream);
        ValidateDimensions(header.USize, header.VSize, maximumDimension, family);
        stream.Position = 0;
        var pif = PifReader.Read(stream);
        if (pif.Encoding != PifTextureEncoding.Indexed8 || pif.PaletteData.Length != 0x400
            || pif.IsSwizzled || pif.MipPixelData.Count != 0)
            throw new InvalidDataException(
                $"{family} PIF images must be unswizzled indexed-8 with a 256-color palette and no mipmaps.");
        return Canonical(header.USize, header.VSize, pif.PaletteData, pif.PixelData);
    }

    private static Indexed8PifImport Canonical(int width, int height, byte[] palette, byte[] pixels) => new(
        width,
        height,
        PifWriter.Write(PifWriter.CreateIndexed8(width, height, palette, pixels)));

    private static void ValidateDimensions(int width, int height, int maximumDimension, string family)
    {
        if (width is <= 0 || width > maximumDimension || height is <= 0 || height > maximumDimension
            || (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            throw new InvalidDataException(
                $"{family} texture dimensions must be powers of two no larger than {maximumDimension}x{maximumDimension}.");
    }
}
