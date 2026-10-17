using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Core.Textures.Palettes;

internal sealed record Rgba32TexturePaletteInput(
    string Key,
    PifTextureEncoding Encoding,
    int PaletteFormat,
    int PaletteOrder,
    int Capacity,
    IReadOnlyList<byte[]> Rgba32Levels,
    IReadOnlyList<OptimizedPaletteEntry>? ReservedEntries = null);

public static partial class PaletteOptimizer
{
    private static Rgba32TexturePaletteInput ValidateRgba32Input(Rgba32TexturePaletteInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 256)
            throw new ArgumentException("Custom texture key is missing or too long.", nameof(input));
        if (input.Encoding is not PifTextureEncoding.Indexed4 and not PifTextureEncoding.Indexed8)
            throw new ArgumentException($"Custom texture {input.Key} encoding is unsupported.", nameof(input));
        if (input.Capacity is <= 0 or > 256
            || input.Encoding == PifTextureEncoding.Indexed4 && input.Capacity > 16)
            throw new ArgumentException($"Custom texture {input.Key} palette capacity is invalid.", nameof(input));
        if (input.Rgba32Levels is null
            || input.Rgba32Levels.Any(value => value is null || value.Length == 0 || value.Length % 4 != 0)
            || input.Rgba32Levels.Sum(value => (long)value.Length / 4) is <= 0 or > MaximumCustomTexels)
            throw new ArgumentException($"Custom texture {input.Key} pixel allocation is invalid.", nameof(input));
        var reserved = input.ReservedEntries ?? [];
        if (reserved.Any(value => value is null
                || value.PaletteIndex < 0
                || value.PaletteIndex >= input.Capacity)
            || reserved.Select(value => value.PaletteIndex).Distinct().Count() != reserved.Count)
            throw new ArgumentException($"Custom texture {input.Key} reserved palette entries are invalid.", nameof(input));
        return input;
    }

    private sealed class CustomTextureData
    {
        private readonly IReadOnlyList<TextureColor>? _pixels;
        private readonly IReadOnlyList<byte[]>? _rgba32Levels;

        public CustomTextureData(CustomTexturePaletteInput input)
        {
            Key = input.Key;
            Encoding = input.Encoding;
            PaletteFormat = input.PaletteFormat;
            PaletteOrder = input.PaletteOrder;
            Capacity = input.Capacity;
            ReservedEntries = input.ReservedEntries;
            _pixels = input.Pixels;
            PixelCount = input.Pixels.Count;
        }

        public CustomTextureData(Rgba32TexturePaletteInput input)
        {
            Key = input.Key;
            Encoding = input.Encoding;
            PaletteFormat = input.PaletteFormat;
            PaletteOrder = input.PaletteOrder;
            Capacity = input.Capacity;
            ReservedEntries = input.ReservedEntries;
            _rgba32Levels = input.Rgba32Levels;
            PixelCount = checked((int)input.Rgba32Levels.Sum(value => (long)value.Length / 4));
        }

        public string Key { get; }
        public PifTextureEncoding Encoding { get; }
        public int PaletteFormat { get; }
        public int PaletteOrder { get; }
        public int Capacity { get; }
        public int PixelCount { get; }
        public IReadOnlyList<OptimizedPaletteEntry>? ReservedEntries { get; }

        public IReadOnlyDictionary<TextureColor, int> BuildHistogram(CancellationToken cancellationToken)
        {
            if (_pixels is not null)
            {
                var histogram = new Dictionary<TextureColor, int>();
                foreach (var color in _pixels)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    histogram[color] = histogram.GetValueOrDefault(color) + 1;
                }
                return histogram;
            }

            var packedHistogram = new Dictionary<uint, int>();
            foreach (var level in _rgba32Levels!)
            {
                for (var offset = 0; offset < level.Length; offset += 4)
                {
                    if ((offset & 0x3ffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var color = PackRgba32(level, offset);
                    packedHistogram[color] = packedHistogram.GetValueOrDefault(color) + 1;
                }
            }
            return packedHistogram.ToDictionary(value => Unpack(value.Key), value => value.Value);
        }

        public IReadOnlyList<int> Materialize(
            IReadOnlyDictionary<TextureColor, int> resolvedIndices,
            CancellationToken cancellationToken)
        {
            var indices = new int[PixelCount];
            if (_pixels is not null)
            {
                for (var index = 0; index < _pixels.Count; index++)
                {
                    if ((index & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    indices[index] = resolvedIndices[_pixels[index]];
                }
                return indices;
            }

            var resolvedPacked = resolvedIndices.ToDictionary(value => Pack(value.Key), value => value.Value);
            var indexOffset = 0;
            foreach (var level in _rgba32Levels!)
            {
                for (var offset = 0; offset < level.Length; offset += 4)
                {
                    if ((offset & 0x3ffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    indices[indexOffset++] = resolvedPacked[PackRgba32(level, offset)];
                }
            }
            return indices;
        }

        private static uint PackRgba32(byte[] rgba, int offset) =>
            rgba[offset]
            | (uint)rgba[offset + 1] << 8
            | (uint)rgba[offset + 2] << 16
            | (uint)rgba[offset + 3] << 24;
    }
}
