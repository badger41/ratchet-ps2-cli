using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Core.Textures.Palettes;

public sealed record PaletteOptimizationProfile(string MappingVersion, int Strength)
{
    public const string CurrentMappingVersion = "paletteOptimization.v1";
    public const int DefaultStrength = 50;
    public const double MaximumError = 0.05;

    public static PaletteOptimizationProfile Default { get; } =
        new(CurrentMappingVersion, DefaultStrength);

    public double ReuseThreshold => MaximumError * Math.Pow(Strength / 100d, 2);
}

public sealed record CustomTexturePaletteInput(
    string Key,
    PifTextureEncoding Encoding,
    int PaletteFormat,
    int PaletteOrder,
    int Capacity,
    IReadOnlyList<TextureColor> Pixels,
    IReadOnlyList<OptimizedPaletteEntry>? ReservedEntries = null);

public sealed record CustomTexturePaletteAssignment(
    string TextureKey,
    int PaletteIndex,
    IReadOnlyList<int> PixelIndices,
    int SourceDistinctColorCount,
    int OutputDistinctColorCount,
    int ReusedImportedColorCount,
    long ReusedImportedTexelCount,
    int NewPaletteEntryCount,
    double MeanSquaredError,
    double MaximumSquaredError);

public sealed record JointPaletteOptimizationResult(
    int SchemaVersion,
    string Method,
    bool IsProvenOptimal,
    string MappingVersion,
    int Strength,
    double ReuseThreshold,
    IReadOnlyList<OptimizedPalette> Palettes,
    IReadOnlyList<TexturePaletteAssignment> ImportedAssignments,
    IReadOnlyList<CustomTexturePaletteAssignment> CustomAssignments,
    IReadOnlyList<PaletteOptimizationViolation> Violations,
    IReadOnlyList<string> Warnings);

public static partial class PaletteOptimizer
{
    public const string JointMethod = "deterministic-fixed-centroid";
    private const int MaximumCustomTexels = 16_777_216;

    public static JointPaletteOptimizationResult Optimize(
        TextureInventory importedInventory,
        IEnumerable<CustomTexturePaletteInput> customTextures,
        PaletteOptimizationProfile? profile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedInventory);
        ArgumentNullException.ThrowIfNull(customTextures);
        profile ??= PaletteOptimizationProfile.Default;
        ValidateProfile(profile);
        var custom = customTextures.Select(ValidateCustomInput)
            .OrderBy(value => value.Encoding)
            .ThenBy(value => value.PaletteFormat)
            .ThenBy(value => value.PaletteOrder)
            .ThenBy(value => value.Capacity)
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
        var duplicate = custom.GroupBy(value => value.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate custom texture key {duplicate.Key}.", nameof(customTextures));

        var imported = Optimize(importedInventory, cancellationToken);
        if (imported.Violations.Count > 0)
            return new(
                SchemaVersion,
                JointMethod,
                false,
                profile.MappingVersion,
                profile.Strength,
                profile.ReuseThreshold,
                imported.Palettes,
                imported.Assignments,
                [],
                imported.Violations,
                []);

        var palettes = imported.Palettes.Select(MutablePalette.FromImported).ToList();
        var assignments = new List<CustomTexturePaletteAssignment>(custom.Length);
        var warnings = new List<string>();
        foreach (var texture in custom)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compatibility = CompatibilityOf(texture);
            var histogram = texture.Pixels.GroupBy(value => value)
                .ToDictionary(group => group.Key, group => group.Count());
            var candidates = palettes
                .Where(value => value.Compatibility == compatibility)
                .Select(value => Evaluate(
                    texture, histogram, value, profile.ReuseThreshold, cancellationToken))
                .Where(value => value.MaximumSquaredError <= profile.ReuseThreshold + double.Epsilon)
                .ToArray();
            var selected = candidates
                .OrderBy(value => value.MeanSquaredError)
                .ThenBy(value => value.MaximumSquaredError)
                .ThenBy(value => value.AddedEntries.Count)
                .ThenBy(value => value.Palette.Index)
                .FirstOrDefault();
            if (selected is null)
            {
                var palette = MutablePalette.Create(
                    palettes.Count,
                    compatibility,
                    texture.ReservedEntries ?? []);
                selected = Evaluate(
                    texture, histogram, palette, profile.ReuseThreshold, cancellationToken);
                palettes.Add(palette);
            }

            selected = selected.Materialize(texture.Pixels, cancellationToken);
            selected.Apply(texture.Key);
            assignments.Add(selected.Assignment(texture.Key));
            if (selected.MaximumSquaredError >= PaletteOptimizationProfile.MaximumError)
                warnings.Add($"Custom texture {texture.Key} reached the maximum quantization error bound.");
        }

        return new(
            SchemaVersion,
            JointMethod,
            custom.Length == 0 && imported.IsProvenOptimal,
            profile.MappingVersion,
            profile.Strength,
            profile.ReuseThreshold,
            palettes.Select(value => value.ToResult()).ToArray(),
            imported.Assignments,
            assignments.OrderBy(value => value.TextureKey, StringComparer.Ordinal).ToArray(),
            [],
            warnings);
    }

    private static void ValidateProfile(PaletteOptimizationProfile profile)
    {
        if (profile.MappingVersion != PaletteOptimizationProfile.CurrentMappingVersion)
            throw new ArgumentException($"Unsupported palette optimization mapping {profile.MappingVersion}.", nameof(profile));
        if (profile.Strength is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(profile), "Palette optimization strength must be from 0 through 100.");
    }

    private static CustomTexturePaletteInput ValidateCustomInput(CustomTexturePaletteInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 256)
            throw new ArgumentException("Custom texture key is missing or too long.", nameof(input));
        if (input.Encoding is not PifTextureEncoding.Indexed4 and not PifTextureEncoding.Indexed8)
            throw new ArgumentException($"Custom texture {input.Key} encoding is unsupported.", nameof(input));
        if (input.Capacity is <= 0 or > 256
            || input.Encoding == PifTextureEncoding.Indexed4 && input.Capacity > 16)
            throw new ArgumentException($"Custom texture {input.Key} palette capacity is invalid.", nameof(input));
        if (input.Pixels is null || input.Pixels.Count is <= 0 or > MaximumCustomTexels
            || input.Pixels.Any(value => value is null))
            throw new ArgumentException($"Custom texture {input.Key} pixel allocation is invalid.", nameof(input));
        var reserved = input.ReservedEntries ?? [];
        if (reserved.Any(value => value is null
                || value.PaletteIndex < 0
                || value.PaletteIndex >= input.Capacity)
            || reserved.Select(value => value.PaletteIndex).Distinct().Count() != reserved.Count)
            throw new ArgumentException($"Custom texture {input.Key} reserved palette entries are invalid.", nameof(input));
        return input;
    }

    private static Compatibility CompatibilityOf(CustomTexturePaletteInput texture) => new(
        texture.Encoding,
        texture.PaletteFormat,
        texture.PaletteOrder,
        texture.Capacity);

    private static CandidateEvaluation Evaluate(
        CustomTexturePaletteInput texture,
        IReadOnlyDictionary<TextureColor, int> histogram,
        MutablePalette palette,
        double reuseThreshold,
        CancellationToken cancellationToken)
    {
        var entries = new SortedDictionary<int, TargetEntry>(palette.Entries);
        foreach (var reserved in texture.ReservedEntries ?? [])
        {
            if (entries.TryGetValue(reserved.PaletteIndex, out var existing)
                && existing.Color != reserved.Color)
                return CandidateEvaluation.Invalid(palette);
            entries[reserved.PaletteIndex] = existing is null
                ? new(reserved.Color, true, false)
                : existing with { Reserved = true };
        }

        var fixedMappings = new Dictionary<TextureColor, int>();
        var remaining = new HashSet<TextureColor>();
        foreach (var color in histogram.Keys.OrderBy(Pack))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exact = entries.FirstOrDefault(value => value.Value.Color == color);
            if (!exact.Equals(default(KeyValuePair<int, TargetEntry>)))
            {
                fixedMappings[color] = exact.Key;
                continue;
            }
            var imported = Nearest(color, entries.Where(value => value.Value.Imported));
            if (imported is { } match && match.Distance <= reuseThreshold)
                fixedMappings[color] = match.Index;
            else
                remaining.Add(color);
        }

        var freeIndexes = Enumerable.Range(0, texture.Capacity)
            .Where(index => !entries.ContainsKey(index))
            .ToArray();
        IReadOnlyList<TextureColor> additions;
        if (remaining.Count <= freeIndexes.Length)
        {
            additions = remaining.OrderBy(Pack).ToArray();
        }
        else if (freeIndexes.Length > 0)
        {
            var pixels = texture.Pixels.Where(remaining.Contains).ToArray();
            additions = AlphaAwareKMeans.Quantize(pixels, freeIndexes.Length, cancellationToken)
                .Palette.Distinct()
                .Where(color => entries.Values.All(entry => entry.Color != color))
                .Take(freeIndexes.Length)
                .ToArray();
        }
        else
        {
            additions = [];
        }
        for (var index = 0; index < additions.Count; index++)
            entries.Add(freeIndexes[index], new(additions[index], false, false));

        if (entries.Count == 0) return CandidateEvaluation.Invalid(palette);
        var resolved = new Dictionary<TextureColor, int>();
        foreach (var color in histogram.Keys)
            resolved[color] = fixedMappings.GetValueOrDefault(color, Nearest(color, entries)!.Index);
        double totalError = 0;
        double maximumError = 0;
        long reusedImportedTexels = 0;
        var reusedImportedColors = 0;
        foreach (var (color, frequency) in histogram)
        {
            var targetIndex = resolved[color];
            var target = entries[targetIndex];
            var error = AlphaAwareKMeans.DistanceSquared(color, target.Color);
            totalError += error * frequency;
            maximumError = Math.Max(maximumError, error);
            if (!target.Imported) continue;
            reusedImportedColors++;
            reusedImportedTexels += frequency;
        }
        return new(
            palette,
            entries,
            additions,
            resolved,
            [],
            histogram.Count,
            resolved.Values.Distinct().Count(),
            reusedImportedColors,
            reusedImportedTexels,
            totalError / texture.Pixels.Count,
            maximumError);
    }

    private static NearestEntry? Nearest(
        TextureColor color,
        IEnumerable<KeyValuePair<int, TargetEntry>> entries)
    {
        NearestEntry? best = null;
        foreach (var entry in entries)
        {
            var distance = AlphaAwareKMeans.DistanceSquared(color, entry.Value.Color);
            if (best is not null
                && (distance > best.Distance || distance == best.Distance && entry.Key > best.Index))
                continue;
            best = new(entry.Key, distance);
        }
        return best;
    }

    private sealed record TargetEntry(TextureColor Color, bool Reserved, bool Imported);

    private sealed record NearestEntry(int Index, double Distance);

    private sealed class MutablePalette
    {
        private MutablePalette(
            int index,
            Compatibility compatibility,
            SortedDictionary<int, TargetEntry> entries,
            IEnumerable<string> textureKeys)
        {
            Index = index;
            Compatibility = compatibility;
            Entries = entries;
            TextureKeys = textureKeys.ToHashSet(StringComparer.Ordinal);
        }

        public int Index { get; }
        public Compatibility Compatibility { get; }
        public SortedDictionary<int, TargetEntry> Entries { get; set; }
        public HashSet<string> TextureKeys { get; }

        public static MutablePalette FromImported(OptimizedPalette palette) => new(
            palette.PaletteIndex,
            new(palette.Encoding, palette.PaletteFormat, palette.PaletteOrder, palette.Capacity),
            new(palette.Entries.ToDictionary(
                value => value.PaletteIndex,
                value => new TargetEntry(value.Color, value.Reserved, true))),
            palette.TextureKeys);

        public static MutablePalette Create(
            int index,
            Compatibility compatibility,
            IEnumerable<OptimizedPaletteEntry> reserved) => new(
                index,
                compatibility,
                new(reserved.ToDictionary(
                    value => value.PaletteIndex,
                    value => new TargetEntry(value.Color, true, false))),
                []);

        public OptimizedPalette ToResult() => new(
            Index,
            Compatibility.Encoding,
            Compatibility.PaletteFormat,
            Compatibility.PaletteOrder,
            Compatibility.Capacity,
            Entries.Select(value => new OptimizedPaletteEntry(
                value.Key, value.Value.Color, value.Value.Reserved)).ToArray(),
            TextureKeys.Order(StringComparer.Ordinal).ToArray());
    }

    private sealed record CandidateEvaluation(
        MutablePalette Palette,
        SortedDictionary<int, TargetEntry> Entries,
        IReadOnlyList<TextureColor> AddedEntries,
        IReadOnlyDictionary<TextureColor, int> ResolvedIndices,
        IReadOnlyList<int> PixelIndices,
        int SourceDistinctColorCount,
        int OutputDistinctColorCount,
        int ReusedImportedColorCount,
        long ReusedImportedTexelCount,
        double MeanSquaredError,
        double MaximumSquaredError)
    {
        public static CandidateEvaluation Invalid(MutablePalette palette) => new(
            palette, [], [], new Dictionary<TextureColor, int>(), [],
            0, 0, 0, 0, double.PositiveInfinity, double.PositiveInfinity);

        public CandidateEvaluation Materialize(
            IReadOnlyList<TextureColor> pixels,
            CancellationToken cancellationToken)
        {
            var indices = new int[pixels.Count];
            for (var index = 0; index < pixels.Count; index++)
            {
                if ((index & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                indices[index] = ResolvedIndices[pixels[index]];
            }
            return this with { PixelIndices = indices };
        }

        public void Apply(string textureKey)
        {
            Palette.Entries = Entries;
            Palette.TextureKeys.Add(textureKey);
        }

        public CustomTexturePaletteAssignment Assignment(string textureKey) => new(
            textureKey,
            Palette.Index,
            PixelIndices,
            SourceDistinctColorCount,
            OutputDistinctColorCount,
            ReusedImportedColorCount,
            ReusedImportedTexelCount,
            AddedEntries.Count,
            MeanSquaredError,
            MaximumSquaredError);
    }
}
