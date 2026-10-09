// Ported from deadlocked-level-packer's PngQuantizer.
// Copyright (c) 2020 Daniel Gerendasy. Licensed under the MIT License.
// The complete notice is retained in THIRD_PARTY_NOTICES.md.

namespace RatchetPs2.Core.Textures.Palettes;

public sealed record AlphaAwareQuantization(
    IReadOnlyList<TextureColor> Palette,
    IReadOnlyList<int> Indices);

public static class AlphaAwareKMeans
{
    public const int Seed = 1337;
    public const int IterationCount = 12;
    public const double AlphaWeight = 2.5;

    public static double DistanceSquared(TextureColor left, TextureColor right) =>
        VectorDistanceSquared(ToPremultipliedVector(left), ToPremultipliedVector(right));

    public static AlphaAwareQuantization Quantize(
        IReadOnlyList<TextureColor> pixels,
        int paletteSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (paletteSize <= 0) throw new ArgumentOutOfRangeException(nameof(paletteSize));

        var histogram = new Dictionary<TextureColor, int>();
        foreach (var color in pixels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            histogram[color] = histogram.GetValueOrDefault(color) + 1;
        }

        var hasFullyTransparent = histogram.Keys.Any(color => color.Alpha == 0);
        var reservedTransparent = hasFullyTransparent && paletteSize > 1 ? 1 : 0;
        var workingPaletteSize = paletteSize - reservedTransparent;
        var uniqueColors = histogram.Keys
            .Where(color => color.Alpha > 0 || reservedTransparent == 0)
            .ToArray();
        var finalPalette = new List<TextureColor>(paletteSize);

        if (reservedTransparent == 1) finalPalette.Add(Transparent);

        if (uniqueColors.Length <= workingPaletteSize)
        {
            finalPalette.AddRange(uniqueColors);
        }
        else
        {
            var centroids = BuildInitialCentroids(
                uniqueColors, histogram, workingPaletteSize, cancellationToken);
            for (var iteration = 0; iteration < IterationCount; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sumRed = new double[workingPaletteSize];
                var sumGreen = new double[workingPaletteSize];
                var sumBlue = new double[workingPaletteSize];
                var sumAlpha = new double[workingPaletteSize];
                var counts = new double[workingPaletteSize];

                for (var colorIndex = 0; colorIndex < uniqueColors.Length; colorIndex++)
                {
                    if ((colorIndex & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var color = uniqueColors[colorIndex];
                    var weight = histogram[color];
                    var nearest = FindNearestCentroid(color, centroids);
                    var vector = ToPremultipliedVector(color);
                    sumRed[nearest] += vector.Red * weight;
                    sumGreen[nearest] += vector.Green * weight;
                    sumBlue[nearest] += vector.Blue * weight;
                    sumAlpha[nearest] += vector.Alpha * weight;
                    counts[nearest] += weight;
                }

                for (var index = 0; index < workingPaletteSize; index++)
                {
                    if (counts[index] <= 0) continue;
                    centroids[index] = new(
                        (float)(sumRed[index] / counts[index]),
                        (float)(sumGreen[index] / counts[index]),
                        (float)(sumBlue[index] / counts[index]),
                        (float)(sumAlpha[index] / counts[index]));
                }
            }

            finalPalette.AddRange(centroids.Select(FromPremultipliedVector));
        }

        while (finalPalette.Count < paletteSize) finalPalette.Add(Transparent);

        var palette = finalPalette.Take(paletteSize).ToArray();
        var indices = new int[pixels.Count];
        for (var index = 0; index < pixels.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indices[index] = FindNearestPaletteIndex(pixels[index], palette);
        }

        SortPaletteAndRemapIndices(ref palette, indices);
        return new(palette, indices);
    }

    private static ColorVector[] BuildInitialCentroids(
        TextureColor[] colors,
        IReadOnlyDictionary<TextureColor, int> histogram,
        int count,
        CancellationToken cancellationToken)
    {
        var random = new Random(Seed);
        var centroids = new ColorVector[count];
        var first = colors.OrderByDescending(color => histogram[color]).First();
        centroids[0] = ToPremultipliedVector(first);

        var distances = Enumerable.Repeat(double.MaxValue, colors.Length).ToArray();
        for (var centroidIndex = 1; centroidIndex < count; centroidIndex++)
        {
            var total = 0d;
            for (var colorIndex = 0; colorIndex < colors.Length; colorIndex++)
            {
                if ((colorIndex & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                var vector = ToPremultipliedVector(colors[colorIndex]);
                distances[colorIndex] = Math.Min(
                    distances[colorIndex],
                    VectorDistanceSquared(vector, centroids[centroidIndex - 1]));
                total += distances[colorIndex] * histogram[colors[colorIndex]];
            }

            if (total <= 0)
            {
                centroids[centroidIndex] = ToPremultipliedVector(colors[random.Next(colors.Length)]);
                continue;
            }

            var pick = random.NextDouble() * total;
            var accumulated = 0d;
            var pickedIndex = 0;
            for (var colorIndex = 0; colorIndex < colors.Length; colorIndex++)
            {
                if ((colorIndex & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                accumulated += distances[colorIndex] * histogram[colors[colorIndex]];
                if (accumulated < pick) continue;
                pickedIndex = colorIndex;
                break;
            }
            centroids[centroidIndex] = ToPremultipliedVector(colors[pickedIndex]);
        }

        return centroids;
    }

    private static int FindNearestCentroid(TextureColor color, IReadOnlyList<ColorVector> centroids)
    {
        var vector = ToPremultipliedVector(color);
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < centroids.Count; index++)
        {
            var distance = VectorDistanceSquared(vector, centroids[index]);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = index;
        }
        return bestIndex;
    }

    private static int FindNearestPaletteIndex(TextureColor color, IReadOnlyList<TextureColor> palette)
    {
        var vector = ToPremultipliedVector(color);
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < palette.Count; index++)
        {
            var distance = VectorDistanceSquared(vector, ToPremultipliedVector(palette[index]));
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = index;
        }
        return bestIndex;
    }

    private static ColorVector ToPremultipliedVector(TextureColor color)
    {
        var alpha = color.Alpha / 255f;
        return new(
            color.Red / 255f * alpha,
            color.Green / 255f * alpha,
            color.Blue / 255f * alpha,
            alpha);
    }

    private static TextureColor FromPremultipliedVector(ColorVector vector)
    {
        var alpha = Math.Clamp(vector.Alpha, 0f, 1f);
        if (alpha <= 0.0001f) return Transparent;
        var inverseAlpha = 1f / alpha;
        return new(
            RoundByte(Math.Clamp(vector.Red * inverseAlpha, 0f, 1f)),
            RoundByte(Math.Clamp(vector.Green * inverseAlpha, 0f, 1f)),
            RoundByte(Math.Clamp(vector.Blue * inverseAlpha, 0f, 1f)),
            RoundByte(alpha));
    }

    private static byte RoundByte(float value) => (byte)(value * 255f + 0.5f);

    private static double VectorDistanceSquared(ColorVector left, ColorVector right)
    {
        double red = left.Red - right.Red;
        double green = left.Green - right.Green;
        double blue = left.Blue - right.Blue;
        double alpha = left.Alpha - right.Alpha;
        return red * red + green * green + blue * blue + AlphaWeight * alpha * alpha;
    }

    private static void SortPaletteAndRemapIndices(ref TextureColor[] palette, int[] indices)
    {
        var sorted = palette.Select((color, oldIndex) => new
            {
                Color = color,
                OldIndex = oldIndex,
                Key = PaletteSortKey(color),
            })
            .OrderBy(entry => entry.Color == Transparent ? 1 : 0)
            .ThenBy(entry => entry.Key)
            .ThenBy(entry => entry.OldIndex)
            .ToArray();
        var remap = new int[palette.Length];
        var newPalette = new TextureColor[palette.Length];
        for (var newIndex = 0; newIndex < sorted.Length; newIndex++)
        {
            newPalette[newIndex] = sorted[newIndex].Color;
            remap[sorted[newIndex].OldIndex] = newIndex;
        }
        for (var index = 0; index < indices.Length; index++) indices[index] = remap[indices[index]];
        palette = newPalette;
    }

    private static uint PaletteSortKey(TextureColor color) =>
        (uint)color.Red << 24 | (uint)color.Green << 16 | (uint)color.Blue << 8 | color.Alpha;

    private static TextureColor Transparent { get; } = new(0, 0, 0, 0);

    private readonly record struct ColorVector(float Red, float Green, float Blue, float Alpha);
}
