using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Core.Textures.Palettes;

public static class PaletteTextureWriter
{
    public static byte[] RewritePif(
        ReadOnlySpan<byte> sourcePif,
        TexturePaletteAssignment assignment,
        OptimizedPalette palette)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(palette);
        if (assignment.PaletteIndex != palette.PaletteIndex)
            throw new ArgumentException("Texture assignment references a different optimized palette.");

        var source = PifReader.Read(sourcePif);
        if (source.Encoding != palette.Encoding)
            throw new InvalidDataException("Texture encoding does not match its optimized palette.");
        var expectedCapacity = source.Encoding == PifTextureEncoding.Indexed4 ? 16 : 256;
        if (palette.Capacity != expectedCapacity)
            throw new InvalidDataException("Optimized palette capacity is not valid for the PIF encoding.");

        var paletteBytes = new byte[checked(palette.Capacity * 4)];
        foreach (var entry in palette.Entries)
        {
            if (entry.PaletteIndex < 0 || entry.PaletteIndex >= palette.Capacity)
                throw new InvalidDataException("Optimized palette entry is outside its capacity.");
            var offset = entry.PaletteIndex * 4;
            paletteBytes[offset] = entry.Color.Red;
            paletteBytes[offset + 1] = entry.Color.Green;
            paletteBytes[offset + 2] = entry.Color.Blue;
            paletteBytes[offset + 3] = entry.Color.Alpha;
        }

        var remaps = assignment.IndexRemaps.ToDictionary(value => value.SourcePixelIndex);
        var basePixels = RewritePixels(
            source.Encoding, source.PixelData, source.Header.USize * source.Header.VSize, remaps);
        var mipWidth = source.Header.USize;
        var mipHeight = source.Header.VSize;
        var mipPixels = source.MipPixelData.Select(value =>
        {
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
            return RewritePixels(source.Encoding, value, mipWidth * mipHeight, remaps);
        }).ToArray();
        var header = source.Header with
        {
            PaletteFormat = source.Encoding == PifTextureEncoding.Indexed8 ? 0 : source.Header.PaletteFormat,
            PaletteOrder = palette.PaletteOrder,
        };
        var rewritten = new PifTextureData(header, source.Encoding, paletteBytes, basePixels, mipPixels);
        Verify(source, rewritten, remaps, palette);
        return PifWriter.Write(rewritten);
    }

    private static byte[] RewritePixels(
        PifTextureEncoding encoding,
        byte[] pixels,
        int texelCount,
        IReadOnlyDictionary<int, TextureIndexRemap> remaps)
    {
        var output = new byte[pixels.Length];
        if (encoding == PifTextureEncoding.Indexed8)
        {
            for (var index = 0; index < pixels.Length; index++)
            {
                if (!remaps.TryGetValue(pixels[index], out var remap))
                    throw new InvalidDataException($"Texture pixel index {pixels[index]} has no optimized mapping.");
                output[index] = TextureConverter.DecodePaletteIndex(checked((byte)remap.TargetPaletteIndex));
            }
            return output;
        }
        if (encoding != PifTextureEncoding.Indexed4)
            throw new NotSupportedException($"Unsupported PIF encoding {encoding}.");
        for (var texel = 0; texel < texelCount; texel++)
        {
            var sourceIndex = pixels[texel / 2] >> (texel % 2 * 4) & 0x0f;
            output[texel / 2] |= (byte)(RemapNibble(sourceIndex, remaps) << (texel % 2 * 4));
        }
        return output;
    }

    private static int RemapNibble(
        int sourceIndex,
        IReadOnlyDictionary<int, TextureIndexRemap> remaps)
    {
        if (!remaps.TryGetValue(sourceIndex, out var remap))
            throw new InvalidDataException($"Texture pixel index {sourceIndex} has no optimized mapping.");
        return remap.TargetPaletteIndex is >= 0 and < 16
            ? remap.TargetPaletteIndex
            : throw new InvalidDataException("Indexed4 optimized index exceeds four bits.");
    }

    private static void Verify(
        PifTextureData source,
        PifTextureData rewritten,
        IReadOnlyDictionary<int, TextureIndexRemap> remaps,
        OptimizedPalette palette)
    {
        var colors = palette.Entries.ToDictionary(value => value.PaletteIndex, value => value.Color);
        foreach (var remap in remaps.Values)
            if (!colors.TryGetValue(remap.TargetPaletteIndex, out var color) || color != remap.Color)
                throw new InvalidDataException("Optimized palette does not preserve a mapped source color.");
        var sourceLevels = new[] { source.PixelData }.Concat(source.MipPixelData).ToArray();
        var targetLevels = new[] { rewritten.PixelData }.Concat(rewritten.MipPixelData).ToArray();
        var width = source.Header.USize;
        var height = source.Header.VSize;
        for (var level = 0; level < sourceLevels.Length; level++)
        {
            if (level > 0)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            for (var texel = 0; texel < width * height; texel++)
            {
                var sourceIndex = source.Encoding == PifTextureEncoding.Indexed8
                    ? sourceLevels[level][texel]
                    : sourceLevels[level][texel / 2] >> (texel % 2 * 4) & 0x0f;
                var targetIndex = source.Encoding == PifTextureEncoding.Indexed8
                    ? TextureConverter.DecodePaletteIndex(targetLevels[level][texel])
                    : targetLevels[level][texel / 2] >> (texel % 2 * 4) & 0x0f;
                if (!remaps.TryGetValue(sourceIndex, out var remap) || remap.TargetPaletteIndex != targetIndex)
                    throw new InvalidDataException("Rewritten texel failed semantic verification.");
            }
        }
    }
}
