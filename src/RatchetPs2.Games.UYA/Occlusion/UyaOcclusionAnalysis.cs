using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Read-only discovery, not a visibility solver or a native-format writer.</summary>
public static class UyaOcclusionAnalyzer
{
    public const int MaximumOctants = 100_000;
    public const int MaximumMappingRecords = 100_000;
    public const int MaximumGridBytes = 32 * 1024 * 1024;
    public const int MaskBytes = 128;
    public const int MaskBits = MaskBytes * 8;

    public static UyaOcclusionAnalysis Analyze(
        ReadOnlySpan<byte> gridBytes,
        ReadOnlySpan<byte> mappingBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (gridBytes.Length > MaximumGridBytes || mappingBytes.Length > 16 + MaximumMappingRecords * 8)
            throw new InvalidDataException("UYA occlusion input exceeds the bounded analysis byte limits.");
        var grid = UyaOcclusionGridReader.Read(gridBytes, MaximumOctants, cancellationToken);
        var mappings = UyaOcclusionMappingsReader.Read(mappingBytes, MaximumMappingRecords, cancellationToken);
        var families = new[] { mappings.Tfrags, mappings.Ties, mappings.Mobys };
        var bitCounts = families.Select(family =>
        {
            var counts = new int[MaskBits];
            foreach (var row in family)
                if (row.BitIndex is >= 0 and < MaskBits) counts[row.BitIndex]++;
            return counts;
        }).ToArray();
        var maskRefs = grid.Octants.GroupBy(cell => cell.MaskIndex).ToDictionary(g => g.Key, g => g.Count());
        var masks = new Dictionary<int, byte[]>();
        var maskHashes = new HashSet<string>(StringComparer.Ordinal);
        var setFrequency = new int[MaskBits];
        var histograms = families.Select(_ => new SortedDictionary<int, int>()).ToArray();
        foreach (var (index, references) in maskRefs.OrderBy(pair => pair.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mask = gridBytes.Slice(checked(grid.MasksOffset + index * MaskBytes), MaskBytes).ToArray();
            masks.Add(index, mask);
            maskHashes.Add(Hash(mask));
            var visibleCounts = new int[families.Length];
            for (var bit = 0; bit < MaskBits; bit++)
            {
                if (!IsSet(mask, bit)) continue;
                setFrequency[bit] += references;
                for (var family = 0; family < families.Length; family++)
                    visibleCounts[family] += bitCounts[family][bit];
            }
            for (var family = 0; family < families.Length; family++)
            {
                var histogram = histograms[family];
                histogram[visibleCounts[family]] = histogram.GetValueOrDefault(visibleCounts[family]) + references;
            }
        }

        var corners = new HashSet<(int X, int Y, int Z)>();
        var cells = new Dictionary<(int X, int Y, int Z), int>();
        using var semanticHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var coordinates = new byte[12];
        foreach (var cell in grid.Octants.OrderBy(cell => cell.Z).ThenBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cells.TryAdd((cell.X, cell.Y, cell.Z), cell.MaskIndex))
                throw new InvalidDataException("UYA occlusion grid contains duplicate cell coordinates.");
            for (var z = 0; z < 2; z++) for (var y = 0; y < 2; y++) for (var x = 0; x < 2; x++)
                corners.Add((cell.X + x, cell.Y + y, cell.Z + z));
            BinaryPrimitives.WriteInt32LittleEndian(coordinates, cell.X);
            BinaryPrimitives.WriteInt32LittleEndian(coordinates.AsSpan(4), cell.Y);
            BinaryPrimitives.WriteInt32LittleEndian(coordinates.AsSpan(8), cell.Z);
            semanticHash.AppendData(coordinates);
            semanticHash.AppendData(masks[cell.MaskIndex]);
        }
        long neighborPairs = 0, equalNeighbors = 0, hammingTotal = 0;
        foreach (var ((x, y, z), index) in cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompareNeighbor((x + 1, y, z), index);
            CompareNeighbor((x, y + 1, z), index);
            CompareNeighbor((x, y, z + 1), index);
        }
        void CompareNeighbor((int X, int Y, int Z) position, int index)
        {
            if (!cells.TryGetValue(position, out var other)) return;
            neighborPairs++;
            var hamming = 0;
            for (var offset = 0; offset < MaskBytes; offset++)
                hamming += BitOperations.PopCount((uint)(masks[index][offset] ^ masks[other][offset]));
            if (hamming == 0) equalNeighbors++;
            hammingTotal += hamming;
        }

        var all = families.SelectMany(family => family).ToArray();
        var familyReports = families.Select((rows, family) =>
        {
            var histogram = histograms[family];
            var counts = bitCounts[family];
            var bits = Enumerable.Range(0, MaskBits).Where(bit => counts[bit] != 0).ToArray();
            return new UyaOcclusionFamilyAnalysis(
                new[] { "tfrag", "tie", "moby" }[family], rows.Count,
                rows.Select(row => row.OcclusionId).Distinct().Count(),
                rows.GroupBy(row => row.OcclusionId).Count(group => group.Select(row => row.BitIndex).Distinct().Count() > 1),
                bits.Length, bits.Cast<int?>().FirstOrDefault(), bits.Cast<int?>().LastOrDefault(),
                rows.Where(row => row.BitIndex is < 0 or >= MaskBits).GroupBy(row => row.BitIndex)
                    .OrderBy(group => group.Key).Select(group => new UyaOcclusionBitValue(group.Key, group.Count())).ToArray(),
                bits.Count(bit => setFrequency[bit] == 0),
                bits.Count(bit => grid.Octants.Count != 0 && setFrequency[bit] == grid.Octants.Count),
                histogram.Count == 0 ? 0 : histogram.First().Key,
                Percentile(histogram, grid.Octants.Count, .5), Percentile(histogram, grid.Octants.Count, .95),
                histogram.Count == 0 ? 0 : histogram.Last().Key,
                grid.Octants.Count == 0 ? 0 : histogram.Sum(pair => (long)pair.Key * pair.Value) / (double)grid.Octants.Count,
                rows.GroupBy(row => row.BitIndex).Select(group => group.Count()).DefaultIfEmpty().Max());
        }).ToArray();
        var mapped = Enumerable.Range(0, MaskBits).Where(bit => bitCounts.Any(family => family[bit] != 0)).ToHashSet();
        cancellationToken.ThrowIfCancellationRequested();
        var noEdit = UyaOcclusionMappingsWriter.FilterTfrags(mappingBytes, Enumerable.Range(0, mappings.Tfrags.Count).ToArray());
        return new(
            gridBytes.Length, grid.MasksOffset, grid.Octants.Count, masks.Count, maskHashes.Count,
            maskRefs.Keys.DefaultIfEmpty(-1).Max(), Hash(gridBytes), Hash(mappingBytes),
            Convert.ToHexString(semanticHash.GetHashAndReset()).ToLowerInvariant(),
            mappings.Padding, mappings.TrailingBytes.Length, noEdit.AsSpan().SequenceEqual(mappingBytes),
            grid.Octants.Count == 0 ? null : [grid.Octants.Min(cell => cell.X), grid.Octants.Min(cell => cell.Y), grid.Octants.Min(cell => cell.Z)],
            grid.Octants.Count == 0 ? null : [grid.Octants.Max(cell => cell.X), grid.Octants.Max(cell => cell.Y), grid.Octants.Max(cell => cell.Z)],
            mapped.Count, Enumerable.Range(0, MaskBits).Where(bit => setFrequency[bit] > 0 && !mapped.Contains(bit)).ToArray(),
            mapped.Count(bit => bitCounts.Count(family => family[bit] > 0) > 1),
            all.GroupBy(row => row.BitIndex).Select(group => group.Count()).DefaultIfEmpty().Max(),
            corners.Count, corners.Count + grid.Octants.Count, neighborPairs, equalNeighbors,
            neighborPairs == 0 ? 0 : hammingTotal / (double)neighborPairs, familyReports);
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool IsSet(byte[] mask, int bit) => (mask[bit / 8] & (1 << (bit & 7))) != 0;

    private static int Percentile(SortedDictionary<int, int> histogram, int count, double percentile)
    {
        var remaining = (int)Math.Ceiling(count * percentile);
        foreach (var (value, frequency) in histogram)
        {
            remaining -= frequency;
            if (remaining <= 0) return value;
        }
        return 0;
    }
}
