using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Core.Textures.Palettes;

public sealed record OptimizedPaletteEntry(
    int PaletteIndex,
    TextureColor Color,
    bool Reserved);

public sealed record OptimizedPalette(
    int PaletteIndex,
    PifTextureEncoding Encoding,
    int PaletteFormat,
    int PaletteOrder,
    int Capacity,
    IReadOnlyList<OptimizedPaletteEntry> Entries,
    IReadOnlyList<string> TextureKeys);

public sealed record TextureIndexRemap(
    int SourcePixelIndex,
    int SourcePaletteIndex,
    int TargetPaletteIndex,
    TextureColor Color);

public sealed record TexturePaletteAssignment(
    string TextureKey,
    int PaletteIndex,
    IReadOnlyList<TextureIndexRemap> IndexRemaps);

public sealed record PaletteOptimizationViolation(
    string TextureKey,
    string Code,
    string Message);

public sealed record PaletteOptimizationResult(
    int SchemaVersion,
    string Method,
    bool IsProvenOptimal,
    IReadOnlyList<OptimizedPalette> Palettes,
    IReadOnlyList<TexturePaletteAssignment> Assignments,
    IReadOnlyList<PaletteOptimizationViolation> Violations);

public static class PaletteOptimizer
{
    public const int SchemaVersion = 1;
    public const string ExactMethod = "exact-branch-and-bound";
    public const string HeuristicMethod = "deterministic-overlap-best-fit";
    public const string HybridMethod = "hybrid-exact-and-deterministic-overlap-best-fit";
    private const int ExactTextureLimit = 12;
    private const int ExactNodeLimit = 250_000;

    public static PaletteOptimizationResult Optimize(
        TextureInventory inventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var textures = inventory.Textures.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        var duplicate = textures.GroupBy(value => value.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate texture inventory key {duplicate.Key}.", nameof(inventory));

        var requirements = textures.Select(BuildRequirement).ToArray();
        var violations = requirements
            .Where(value => value.RequiredSlotCount > value.Compatibility.Capacity)
            .Select(value => new PaletteOptimizationViolation(
                value.Texture.Key,
                "palette-capacity-exceeded",
                $"Texture requires {value.RequiredSlotCount} entries but its palette capacity is {value.Compatibility.Capacity}."))
            .ToArray();
        if (violations.Length > 0)
            return new(SchemaVersion, ExactMethod, false, [], [], violations);

        var states = new List<(Compatibility Compatibility, PaletteState State)>();
        var allExact = true;
        var anyExact = false;
        foreach (var group in requirements.GroupBy(value => value.Compatibility).OrderBy(value => value.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = group.OrderByDescending(value => value.RequiredSlotCount)
                .ThenByDescending(value => value.Colors.Count)
                .ThenBy(value => value.Texture.Key, StringComparer.Ordinal)
                .ToArray();
            var heuristic = PackHeuristic(ordered, cancellationToken);
            var exact = TryPackExact(ordered, heuristic, cancellationToken);
            anyExact |= exact.Proven;
            allExact &= exact.Proven;
            states.AddRange(exact.States
                .OrderBy(value => value.TextureKeys.Min(StringComparer.Ordinal), StringComparer.Ordinal)
                .Select(value => (group.Key, value)));
        }

        var palettes = new List<OptimizedPalette>(states.Count);
        var assignments = new List<TexturePaletteAssignment>(requirements.Length);
        for (var paletteIndex = 0; paletteIndex < states.Count; paletteIndex++)
        {
            var (compatibility, state) = states[paletteIndex];
            var layout = BuildLayout(state, compatibility.Capacity);
            palettes.Add(new(
                paletteIndex,
                compatibility.Encoding,
                compatibility.PaletteFormat,
                compatibility.PaletteOrder,
                compatibility.Capacity,
                layout.OrderBy(value => value.Key)
                    .Select(value => new OptimizedPaletteEntry(
                        value.Key, Unpack(value.Value), state.Pinned.ContainsKey(value.Key)))
                    .ToArray(),
                state.TextureKeys.Order(StringComparer.Ordinal).ToArray()));
            foreach (var texture in state.Textures.OrderBy(value => value.Texture.Key, StringComparer.Ordinal))
            {
                var colorIndexes = layout.GroupBy(value => value.Value)
                    .ToDictionary(group => group.Key, group => group.Min(value => value.Key));
                assignments.Add(new(
                    texture.Texture.Key,
                    paletteIndex,
                    texture.Texture.IndexUsages.OrderBy(value => value.SourcePixelIndex)
                        .Select(value => new TextureIndexRemap(
                            value.SourcePixelIndex,
                            value.PaletteIndex,
                            texture.Pinned.ContainsKey(value.PaletteIndex)
                                ? value.PaletteIndex
                                : colorIndexes[Pack(value.Color)],
                            value.Color))
                        .ToArray()));
            }
        }

        var method = allExact ? ExactMethod : anyExact ? HybridMethod : HeuristicMethod;
        return new(
            SchemaVersion,
            method,
            allExact,
            palettes,
            assignments.OrderBy(value => value.TextureKey, StringComparer.Ordinal).ToArray(),
            []);
    }

    private static Requirement BuildRequirement(TextureInventoryEntry texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var constraint = texture.Constraint;
        if (constraint.MaxPaletteEntries is <= 0 or > 256)
            throw new ArgumentException($"Texture {texture.Key} has an invalid palette capacity.");
        if (constraint.Encoding == PifTextureEncoding.Indexed4 && constraint.MaxPaletteEntries > 16)
            throw new ArgumentException($"Texture {texture.Key} exceeds indexed4 palette capacity.");
        if (constraint.Encoding is not PifTextureEncoding.Indexed4 and not PifTextureEncoding.Indexed8)
            throw new ArgumentException($"Texture {texture.Key} has an unsupported encoding.");

        var entries = texture.PaletteEntries.ToDictionary(value => value.PaletteIndex);
        var pinned = new SortedDictionary<int, uint>();
        foreach (var entry in texture.PaletteEntries.Where(value => value.Reserved))
        {
            if (entry.PaletteIndex < 0 || entry.PaletteIndex >= constraint.MaxPaletteEntries)
                throw new ArgumentException($"Texture {texture.Key} has a reserved index outside its capacity.");
            pinned.Add(entry.PaletteIndex, Pack(entry.Color));
        }
        foreach (var usage in texture.IndexUsages)
        {
            if (!entries.TryGetValue(usage.PaletteIndex, out var entry) || entry.Color != usage.Color)
                throw new ArgumentException($"Texture {texture.Key} has inconsistent palette usage {usage.PaletteIndex}.");
        }

        var colors = texture.IndexUsages.Select(value => Pack(value.Color))
            .Concat(pinned.Values)
            .ToHashSet();
        var compatibility = new Compatibility(
            constraint.Encoding,
            constraint.PaletteFormat,
            constraint.PaletteOrder,
            constraint.MaxPaletteEntries);
        return new(texture, compatibility, colors, pinned);
    }

    private static PackResult TryPackExact(
        Requirement[] textures,
        List<PaletteState> heuristic,
        CancellationToken cancellationToken)
    {
        var distinctColors = textures.SelectMany(value => value.Colors).Distinct().Count();
        var lowerBound = Math.Max(1, (distinctColors + textures[0].Compatibility.Capacity - 1)
            / textures[0].Compatibility.Capacity);
        if (lowerBound == heuristic.Count)
            return new(heuristic, true);
        if (textures.Length > ExactTextureLimit)
            return new(heuristic, false);

        var nodes = 0;
        for (var target = lowerBound; target < heuristic.Count; target++)
        {
            var states = new List<PaletteState>();
            if (Search(0, target, textures, states, ref nodes, cancellationToken))
                return new(states, true);
            if (nodes >= ExactNodeLimit)
                return new(heuristic, false);
        }
        return new(heuristic, true);
    }

    private static bool Search(
        int textureIndex,
        int targetPaletteCount,
        Requirement[] textures,
        List<PaletteState> states,
        ref int nodes,
        CancellationToken cancellationToken)
    {
        if (++nodes >= ExactNodeLimit) return false;
        if ((nodes & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
        if (textureIndex == textures.Length) return true;

        var texture = textures[textureIndex];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < states.Count; index++)
        {
            var signature = states[index].Signature;
            if (!seen.Add(signature) || !TryMerge(states[index], texture, out var merged)) continue;
            var original = states[index];
            states[index] = merged;
            if (Search(textureIndex + 1, targetPaletteCount, textures, states, ref nodes, cancellationToken))
                return true;
            states[index] = original;
        }
        if (states.Count >= targetPaletteCount) return false;
        states.Add(PaletteState.Create(texture));
        if (Search(textureIndex + 1, targetPaletteCount, textures, states, ref nodes, cancellationToken))
            return true;
        states.RemoveAt(states.Count - 1);
        return false;
    }

    private static List<PaletteState> PackHeuristic(
        Requirement[] textures,
        CancellationToken cancellationToken)
    {
        var states = new List<PaletteState>();
        var byColor = new Dictionary<uint, HashSet<int>>();
        var bySize = Enumerable.Range(0, textures[0].Compatibility.Capacity + 1)
            .Select(_ => new SortedSet<int>()).ToArray();
        foreach (var texture in textures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overlapping = new HashSet<int>();
            foreach (var color in texture.Colors)
                if (byColor.TryGetValue(color, out var indexes)) overlapping.UnionWith(indexes);

            var bestIndex = -1;
            PaletteState? best = null;
            var bestAdded = int.MaxValue;
            var bestRemaining = int.MaxValue;
            var bestTextureCount = -1;
            foreach (var index in overlapping.Order())
            {
                if (!TryMerge(states[index], texture, out var merged)) continue;
                var added = merged.RequiredSlotCount - states[index].RequiredSlotCount;
                var remaining = texture.Compatibility.Capacity - merged.RequiredSlotCount;
                if (!IsBetter(added, remaining, merged.Textures.Count, index,
                    bestAdded, bestRemaining, bestTextureCount, bestIndex)) continue;
                (bestIndex, best, bestAdded, bestRemaining, bestTextureCount) =
                    (index, merged, added, remaining, merged.Textures.Count);
            }

            if (best is null)
            {
                var largestPossible = texture.Compatibility.Capacity - texture.RequiredSlotCount;
                for (var size = largestPossible; size >= 0 && best is null; size--)
                {
                    foreach (var index in bySize[size])
                    {
                        if (overlapping.Contains(index) || !TryMerge(states[index], texture, out var merged)) continue;
                        var added = merged.RequiredSlotCount - states[index].RequiredSlotCount;
                        var remaining = texture.Compatibility.Capacity - merged.RequiredSlotCount;
                        if (!IsBetter(added, remaining, merged.Textures.Count, index,
                            bestAdded, bestRemaining, bestTextureCount, bestIndex)) continue;
                        (bestIndex, best, bestAdded, bestRemaining, bestTextureCount) =
                            (index, merged, added, remaining, merged.Textures.Count);
                    }
                }
            }

            if (best is not null)
            {
                var previous = states[bestIndex];
                bySize[previous.RequiredSlotCount].Remove(bestIndex);
                states[bestIndex] = best;
                bySize[best.RequiredSlotCount].Add(bestIndex);
                foreach (var color in best.Colors.Where(color => !previous.Colors.Contains(color)))
                {
                    if (!byColor.TryGetValue(color, out var indexes)) byColor.Add(color, indexes = []);
                    indexes.Add(bestIndex);
                }
            }
            else
            {
                var state = PaletteState.Create(texture);
                var index = states.Count;
                states.Add(state);
                bySize[state.RequiredSlotCount].Add(index);
                foreach (var color in state.Colors)
                {
                    if (!byColor.TryGetValue(color, out var indexes)) byColor.Add(color, indexes = []);
                    indexes.Add(index);
                }
            }
        }
        return states;
    }

    private static bool IsBetter(
        int added,
        int remaining,
        int textureCount,
        int index,
        int bestAdded,
        int bestRemaining,
        int bestTextureCount,
        int bestIndex)
    {
        if (added != bestAdded) return added < bestAdded;
        if (remaining != bestRemaining) return remaining < bestRemaining;
        if (textureCount != bestTextureCount) return textureCount > bestTextureCount;
        return index < bestIndex;
    }

    private static bool TryMerge(PaletteState state, Requirement texture, out PaletteState merged)
    {
        foreach (var (index, color) in texture.Pinned)
        {
            if (state.Pinned.TryGetValue(index, out var existing) && existing != color)
            {
                merged = null!;
                return false;
            }
        }
        var pinned = new SortedDictionary<int, uint>(state.Pinned);
        foreach (var value in texture.Pinned) pinned.TryAdd(value.Key, value.Value);
        var colors = new HashSet<uint>(state.Colors);
        colors.UnionWith(texture.Colors);
        var requiredSlots = RequiredSlotCount(colors, pinned);
        if (requiredSlots > texture.Compatibility.Capacity)
        {
            merged = null!;
            return false;
        }
        merged = new(
            state.Textures.Append(texture).ToArray(),
            colors,
            pinned,
            requiredSlots);
        return true;
    }

    private static SortedDictionary<int, uint> BuildLayout(PaletteState state, int capacity)
    {
        var layout = new SortedDictionary<int, uint>(state.Pinned);
        var pinnedColors = state.Pinned.Values.ToHashSet();
        var freeIndexes = Enumerable.Range(0, capacity).Where(index => !layout.ContainsKey(index)).GetEnumerator();
        foreach (var color in state.Colors.Where(color => !pinnedColors.Contains(color)).Order())
        {
            if (!freeIndexes.MoveNext()) throw new InvalidOperationException("Optimized palette exceeds capacity.");
            layout.Add(freeIndexes.Current, color);
        }
        return layout;
    }

    private static int RequiredSlotCount(HashSet<uint> colors, SortedDictionary<int, uint> pinned)
    {
        var pinnedColors = pinned.Values.ToHashSet();
        return pinned.Count + colors.Count(color => !pinnedColors.Contains(color));
    }

    private static uint Pack(TextureColor color) =>
        color.Red | (uint)color.Green << 8 | (uint)color.Blue << 16 | (uint)color.Alpha << 24;

    private static TextureColor Unpack(uint color) => new(
        (byte)color,
        (byte)(color >> 8),
        (byte)(color >> 16),
        (byte)(color >> 24));

    private sealed record Compatibility(
        PifTextureEncoding Encoding,
        int PaletteFormat,
        int PaletteOrder,
        int Capacity) : IComparable<Compatibility>
    {
        public int CompareTo(Compatibility? other)
        {
            if (other is null) return 1;
            var result = Encoding.CompareTo(other.Encoding);
            if (result != 0) return result;
            result = PaletteFormat.CompareTo(other.PaletteFormat);
            if (result != 0) return result;
            result = PaletteOrder.CompareTo(other.PaletteOrder);
            return result != 0 ? result : Capacity.CompareTo(other.Capacity);
        }
    }

    private sealed record Requirement(
        TextureInventoryEntry Texture,
        Compatibility Compatibility,
        HashSet<uint> Colors,
        SortedDictionary<int, uint> Pinned)
    {
        public int RequiredSlotCount => PaletteOptimizer.RequiredSlotCount(Colors, Pinned);
    }

    private sealed record PaletteState(
        IReadOnlyList<Requirement> Textures,
        HashSet<uint> Colors,
        SortedDictionary<int, uint> Pinned,
        int RequiredSlotCount)
    {
        public IReadOnlyList<string> TextureKeys => Textures.Select(value => value.Texture.Key).ToArray();

        public string Signature => string.Join(',', Pinned.Select(value => $"{value.Key}:{value.Value:X8}"))
            + '|' + string.Join(',', Colors.Order().Select(value => value.ToString("X8")));

        public static PaletteState Create(Requirement texture) => new(
            [texture],
            new(texture.Colors),
            new(texture.Pinned),
            texture.RequiredSlotCount);
    }

    private sealed record PackResult(List<PaletteState> States, bool Proven);
}
