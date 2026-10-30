using System.Numerics;

namespace RatchetPs2.Core.Occlusion;

public static class OcclusionSamplePlanner
{
    public const int MaximumCells = 100_000;
    public const int MaximumOrigins = 250_000;
    public static (IReadOnlyList<float[]> Origins, IReadOnlyList<OcclusionCell> Cells) PlanCells(
        IReadOnlyList<OcclusionOctant> octants, CancellationToken token = default,
        OcclusionSampleQualifier? qualifier = null, Action<int, int>? progress = null, int samplePullIn = 0,
        IReadOnlyList<float>? barrierHeights = null)
    {
        if (samplePullIn is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(samplePullIn));
        if (!ValidBarriers(barrierHeights)) throw new InvalidDataException("Invalid occlusion barrier heights.");
        if (octants.Count is < 1 or > MaximumCells)
            throw new InvalidDataException($"Occlusion has {octants.Count:N0} octants; supported range is 1–{MaximumCells:N0}.");
        if (octants.Select(c => (c.X, c.Y, c.Z)).Distinct().Count() != octants.Count)
            throw new InvalidDataException("Occlusion contains duplicate octant coordinates.");
        if (octants.Any(c => new[] { c.X, c.Y, c.Z }.Any(v => v is < -250000 or > 250000)))
            throw new InvalidDataException("Occlusion contains coordinates outside the supported range of -250,000 to 250,000.");
        var origins = new List<float[]>(); var ids = new Dictionary<(float, float, float), int>();
        int Sample(float x, float y, float z)
        {
            if (ids.TryGetValue((x, y, z), out var existing)) return existing;
            if (origins.Count >= MaximumOrigins)
                throw new InvalidDataException($"Occlusion exceeds {MaximumOrigins:N0} shared camera positions.");
            var id = origins.Count; ids.Add((x, y, z), id); origins.Add([x, y, z]); return id;
        }
        var cells = new List<OcclusionCell>(octants.Count);
        foreach (var c in octants)
        {
            token.ThrowIfCancellationRequested();
            int Corner(Vector3 point)
            {
                if (barrierHeights is { Count: > 0 })
                    point = OcclusionSampleQualifier.InsetBarrierBoundary(point, c.Z * 4 + 2, barrierHeights);
                return Sample(point.X, point.Y, point.Z);
            }
            var samples = new List<int> { Sample(c.X * 4 + 2, c.Y * 4 + 2, c.Z * 4 + 2) };
            byte inwardCorners = 0;
            if (qualifier is null)
            {
                for (int z = 0; z <= 4; z += 4) for (int y = 0; y <= 4; y += 4) for (int x = 0; x <= 4; x += 4)
                    samples.Add(Corner(new(c.X * 4 + x, c.Y * 4 + y, c.Z * 4 + z)));
            }
            else foreach (var point in qualifier.Qualify(new Vector3(c.X * 4, c.Y * 4, c.Z * 4), out inwardCorners, token, samplePullIn * .18f))
                samples.Add(Corner(point));
            cells.Add(new(c.X, c.Y, c.Z, c.ReservedVisible, samples, inwardCorners));
            if ((cells.Count & 127) == 0 || cells.Count == octants.Count) progress?.Invoke(cells.Count, octants.Count);
        }
        return (origins, cells);
    }

    private static bool ValidBarriers(IReadOnlyList<float>? heights) => heights is null
        || heights.Count <= 16 && heights.All(h => float.IsFinite(h) && Math.Abs(h) <= 1000000)
            && heights.Distinct().Count() == heights.Count;
}
