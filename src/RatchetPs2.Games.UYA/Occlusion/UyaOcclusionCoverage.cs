using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.Occlusion;
using static RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionCoverage
{
    public static OcclusionCoverageProfile Profile { get; } = new(2, 4, 0, 131_069, 1_024,
        OcclusionSamplePlanner.MaximumCells, 2_000_000, 0.00001);

    public static OcclusionCoveragePreview Preview(IReadOnlyList<OcclusionCoverageCoordinate> primary,
        IReadOnlyList<OcclusionCoverageShape> shapes, CancellationToken token = default)
        => Evaluate(primary, shapes, token, "uya-shape-coverage-v2\0"u8);

    internal static OcclusionCoveragePreview Evaluate(IReadOnlyList<OcclusionCoverageCoordinate> primary,
        IReadOnlyList<OcclusionCoverageShape> shapes, CancellationToken token, ReadOnlySpan<byte> fingerprintPrefix,
        IReadOnlyList<OcclusionCoverageCoordinate>? additions = null)
    {
        ArgumentNullException.ThrowIfNull(primary); ArgumentNullException.ThrowIfNull(shapes);
        token.ThrowIfCancellationRequested();
        if (primary.Count > Profile.MaximumCells || shapes.Count > Profile.MaximumSources)
            throw new InvalidDataException("UYA coverage preview exceeds its source or Primary-cell limit.");
        foreach (var cell in primary) { token.ThrowIfCancellationRequested(); ValidateCoordinate(cell); }
        var prepared = new PreparedShape[shapes.Count];
        long candidates = primary.Count + (additions?.Count ?? 0); var exclusions = 0;
        for (var i = 0; i < shapes.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var shape = Prepare(shapes[i]); prepared[i] = shape;
            if (shape.Exclude) exclusions++;
            else candidates += shape.CandidateCount;
        }
        // Bound every test before enumeration, including exclusion tests against the largest possible union.
        var work = candidates + Math.Min(candidates, Profile.MaximumCells) * exclusions;
        if (work > Profile.MaximumCandidateTests)
            throw new InvalidDataException($"UYA shape coverage preview exceeds {Profile.MaximumCandidateTests:N0} candidate tests; reduce the volume sizes or source count.");

        var union = new HashSet<OcclusionCoverageCoordinate>(primary);
        if (additions is not null) foreach (var cell in additions) { token.ThrowIfCancellationRequested(); ValidateCoordinate(cell); union.Add(cell); }
        if (union.Count > Profile.MaximumCells) throw new InvalidDataException("UYA coverage inclusion union exceeds the cell limit before exclusions.");
        var counts = new int[shapes.Count];
        var tests = primary.Count + (additions?.Count ?? 0);
        void CheckWork() { if ((++tests & 255) == 0) token.ThrowIfCancellationRequested(); }
        for (var i = 0; i < prepared.Length; i++)
        {
            var shape = prepared[i]; if (shape.Exclude) continue;
            for (var z = shape.Minimum.Z; z <= shape.Maximum.Z; z++)
            for (var y = shape.Minimum.Y; y <= shape.Maximum.Y; y++)
            for (var x = shape.Minimum.X; x <= shape.Maximum.X; x++)
            {
                CheckWork(); var cell = new OcclusionCoverageCoordinate(x, y, z);
                if (!shape.Intersects(cell)) continue;
                counts[i]++; union.Add(cell);
                if (union.Count > Profile.MaximumCells)
                    throw new InvalidDataException($"UYA coverage inclusion union exceeds {Profile.MaximumCells:N0} cells before exclusions.");
            }
        }
        var removed = new HashSet<OcclusionCoverageCoordinate>();
        for (var i = 0; i < prepared.Length; i++)
        {
            var shape = prepared[i]; if (!shape.Exclude) continue;
            foreach (var cell in union)
            {
                CheckWork(); if (!shape.Contains(cell)) continue;
                counts[i]++; removed.Add(cell);
            }
        }
        union.ExceptWith(removed);
        var result = union.OrderBy(c => c.Z).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
        token.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(fingerprintPrefix);
        Span<byte> bytes = stackalloc byte[12];
        foreach (var cell in result)
        {
            token.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(bytes, cell.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], cell.Y);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], cell.Z); hash.AppendData(bytes);
        }
        return new(Array.AsReadOnly(result), Array.AsReadOnly(counts), tests, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
