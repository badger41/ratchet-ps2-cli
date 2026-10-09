using System.Buffers.Binary;
using System.Numerics;

namespace RatchetPs2.Core.Fx;

public sealed record FxTextureSourceEntry(
    int Index,
    int PaletteOffset,
    int PixelOffset,
    int Width,
    int Height,
    int PixelLength,
    byte[] PaletteBytes,
    byte[] PixelBytes,
    string? Diagnostic)
{
    public bool IsValid => Diagnostic is null;
}

public static class FxTextureInventoryReader
{
    public const int HeaderLength = 0xc0;
    public const int DefinitionLength = 0x10;
    public const int PaletteLength = 0x400;
    public const int MaximumTextureCount = 4_096;
    public const int MaximumDimension = 4_096;
    public const int MaximumPixelLength = MaximumDimension * MaximumDimension;
    public const int PaletteAlignment = 0x100;
    public const int PixelAlignment = 0x10;

    internal const int CountOffset = 0x58;
    internal const int DefinitionsOffset = 0x5c;
    internal const int DataOffset = 0x68;

    public static IReadOnlyList<FxTextureSourceEntry> Read(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetBytes,
        CancellationToken cancellationToken = default)
    {
        if (headerBytes.Length < HeaderLength)
            throw new InvalidDataException($"FX asset header must contain at least 0x{HeaderLength:X} bytes.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(headerBytes[CountOffset..]);
        var definitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(headerBytes[DefinitionsOffset..]);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(headerBytes[DataOffset..]);
        if (count is < 0 or > MaximumTextureCount)
            throw new InvalidDataException($"FX texture count {count} is outside 0..{MaximumTextureCount}.");
        if (!Contains(headerBytes.Length, definitionsOffset, (long)count * DefinitionLength))
            throw new InvalidDataException(
                $"FX definition table offset 0x{definitionsOffset:X} and count {count} exceed the header.");
        if (dataOffset < 0 || dataOffset > assetBytes.Length)
            throw new InvalidDataException(
                $"FX data offset 0x{dataOffset:X} exceeds the 0x{assetBytes.Length:X}-byte asset payload.");

        var entries = new FxTextureSourceEntry[count];
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = headerBytes.Slice(definitionsOffset + index * DefinitionLength, DefinitionLength);
            var paletteOffset = BinaryPrimitives.ReadInt32LittleEndian(definition);
            var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(definition[4..]);
            var width = BinaryPrimitives.ReadInt32LittleEndian(definition[8..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(definition[12..]);
            entries[index] = ReadEntry(
                index, paletteOffset, pixelOffset, width, height, dataOffset, assetBytes);
        }
        return entries;
    }

    private static FxTextureSourceEntry ReadEntry(
        int index,
        int paletteOffset,
        int pixelOffset,
        int width,
        int height,
        int dataOffset,
        ReadOnlySpan<byte> assetBytes)
    {
        string? diagnostic = null;
        long pixelLength = 0;
        if (width is <= 0 or > MaximumDimension || height is <= 0 or > MaximumDimension
            || !BitOperations.IsPow2((uint)width) || !BitOperations.IsPow2((uint)height))
            diagnostic = $"FX texture {index} dimensions {width}x{height} must be powers of two no larger than {MaximumDimension}.";
        else
            pixelLength = (long)width * height;

        if (diagnostic is null && paletteOffset % PaletteAlignment != 0)
            diagnostic = $"FX texture {index} palette offset 0x{paletteOffset:X} is not 0x{PaletteAlignment:X} aligned.";
        if (diagnostic is null && pixelOffset % PixelAlignment != 0)
            diagnostic = $"FX texture {index} pixel offset 0x{pixelOffset:X} is not 0x{PixelAlignment:X} aligned.";
        if (diagnostic is null
            && (!Contains(assetBytes.Length, (long)dataOffset + paletteOffset, PaletteLength)
                || !Contains(assetBytes.Length, (long)dataOffset + pixelOffset, pixelLength)))
            diagnostic = $"FX texture {index} palette or pixel range exceeds the asset payload.";

        return diagnostic is not null
            ? new(index, paletteOffset, pixelOffset, width, height, checked((int)pixelLength), [], [], diagnostic)
            : new(
                index,
                paletteOffset,
                pixelOffset,
                width,
                height,
                checked((int)pixelLength),
                assetBytes.Slice(checked(dataOffset + paletteOffset), PaletteLength).ToArray(),
                assetBytes.Slice(checked(dataOffset + pixelOffset), checked((int)pixelLength)).ToArray(),
                null);
    }

    private static bool Contains(int totalLength, long offset, long length) =>
        offset >= 0 && length >= 0 && offset <= totalLength && length <= totalLength - offset;
}
