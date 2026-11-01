using RatchetPs2.Core.Occlusion;
using static RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionGraphCoverage
{
    // A measured 52k-cell/three-seed scene needs 97.5m tests; retain a bounded allowance for the 2m-ray profile.
    public static OcclusionGraphCoverageProfile Profile { get; } = new(3, 4096, 16_384, 100_000, 2_000_000, 200_000_000, .001, .001);

    public static OcclusionGraphCoveragePreview Preview(IReadOnlyList<OcclusionCoverageCoordinate> primary,
        IReadOnlyList<OcclusionCoverageShape> shapes, IReadOnlyList<OcclusionCoverageGraph> graphs,
        IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<OcclusionAlphaTexture> textures,
        CancellationToken token = default, Action<int, int>? progress = null,
        IReadOnlyList<OcclusionSurfaceTriangle>? stickyTriangles = null)
    {
        ArgumentNullException.ThrowIfNull(primary); ArgumentNullException.ThrowIfNull(shapes); ArgumentNullException.ThrowIfNull(graphs);
        token.ThrowIfCancellationRequested();
        var geometry = UyaOcclusionCoverage.Profile;
        if ((long)shapes.Count + graphs.Count > geometry.MaximumSources || primary.Count > geometry.MaximumCells)
            throw new InvalidDataException("Graph preview exceeds its source or Primary-cell limit.");
        foreach (var cell in primary) { token.ThrowIfCancellationRequested(); ValidateCoordinate(cell); }
        var prepared = new PreparedShape[graphs.Count]; var seeds = new D3[graphs.Count][];
        long candidates = 0; var seedCount = 0;
        for (var i = 0; i < graphs.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var graph = graphs[i];
            if (graph is null || graph.Seeds is null || graph.Seeds.Count == 0 || graph.Seeds.Count > Profile.MaximumSeedsPerGraph
                || (seedCount += graph.Seeds.Count) > Profile.MaximumSeeds)
                throw new InvalidDataException($"Graph {i + 1} requires 1–{Profile.MaximumSeedsPerGraph} seeds; the combined seed limit is {Profile.MaximumSeeds}.");
            if (graph.StickyDistance is { } distance && (!float.IsFinite(distance) || distance < 0 || distance > 1_000_000))
                throw new InvalidDataException($"Graph {i + 1} sticky distance must be finite and between 0 and 1,000,000.");
            if (graph.StickyDistance.HasValue && stickyTriangles is null)
                throw new InvalidDataException("Sticky graphs require explicit TIE/tfrag surface geometry.");
            if (graph.CylinderRadius is { } radius && (graph.Size.X != radius * 2 || graph.Size.Y != radius * 2))
                throw new InvalidDataException($"Graph {i + 1} cylinder bounds must use its diameter for Size X/Y.");
            var region = Prepare(graph.CylinderRadius is { } r
                ? new OcclusionCoverageCylinder(graph.Center, graph.Rotation, r, graph.Size.Z)
                : new OcclusionCoverageBox(graph.Center, graph.Rotation, graph.Size)); prepared[i] = region;
            var count = region.CandidateCount; candidates += count;
            if (candidates > Profile.MaximumGraphCandidates) throw new InvalidDataException("Graph preview exceeds its candidate limit; reduce regions.");
            // Probes stop after the first reachable seed; the shared query budget bounds actual rays.
            seeds[i] = new D3[graph.Seeds.Count];
            for (var j = 0; j < graph.Seeds.Count; j++)
            {
                token.ThrowIfCancellationRequested(); var seed = UyaOcclusionCoverageQuery.From(graph.Seeds[j]);
                if (!double.IsFinite(seed.X) || !double.IsFinite(seed.Y) || !double.IsFinite(seed.Z)
                    || Math.Abs(seed.X) > graph.Size.X / 2.0 || Math.Abs(seed.Y) > graph.Size.Y / 2.0 || Math.Abs(seed.Z) > graph.Size.Z / 2.0
                    || graph.CylinderRadius is { } seedRadius && seed.X * seed.X + seed.Y * seed.Y > (double)seedRadius * seedRadius)
                    throw new InvalidDataException($"Graph {i + 1} seed {j + 1} must be finite and inside its local region.");
                seeds[i][j] = region.Center + region.Axes[0] * seed.X + region.Axes[1] * seed.Y + region.Axes[2] * seed.Z;
            }
        }
        long inclusion = primary.Count + candidates; var exclusions = 0;
        foreach (var shape in shapes)
        {
            token.ThrowIfCancellationRequested(); var value = Prepare(shape);
            if (value.Exclude) exclusions++; else inclusion += value.CandidateCount;
        }
        if (inclusion + Math.Min(inclusion, geometry.MaximumCells) * exclusions + candidates > geometry.MaximumCandidateTests)
            throw new InvalidDataException("Graph and shape preview exceeds its combined candidate-work limit; reduce regions or shapes.");
        progress?.Invoke(0, Math.Max(1, (int)candidates));
        var query = new UyaOcclusionCoverageQuery(triangles, textures, token);
        UyaOcclusionCoverageQuery? stickyQuery = null;
        if (graphs.Any(g => g.StickyDistance.HasValue))
            stickyQuery = ReferenceEquals(triangles, stickyTriangles) ? query
                : new UyaOcclusionCoverageQuery(stickyTriangles!, textures, token, query.WorkBudget);
        var completed = 0;
        var results = new OcclusionGraphCoverageResult[graphs.Count]; var additions = new HashSet<OcclusionCoverageCoordinate>();
        for (var i = 0; i < prepared.Length; i++)
        {
            for (var j = 0; j < seeds[i].Length; j++)
                if (query.Buried(seeds[i][j]))
                    throw new OcclusionCoverageSeedException(i, j, $"Graph {i + 1} seed {j + 1} appears buried in closed opaque geometry. Move it into reachable camera space or remove it.");
            var region = prepared[i]; var accepted = new List<OcclusionCoverageCoordinate>(); var rejected = new List<OcclusionCoverageCoordinate>();
            for (var z = region.Minimum.Z; z <= region.Maximum.Z; z++)
            for (var y = region.Minimum.Y; y <= region.Maximum.Y; y++)
            for (var x = region.Minimum.X; x <= region.Maximum.X; x++)
            {
                token.ThrowIfCancellationRequested();
                if ((++completed & 255) == 0) progress?.Invoke(completed, (int)candidates);
                var cell = new OcclusionCoverageCoordinate(x, y, z);
                if (!region.Intersects(cell)) continue;
                var center = new D3((x + .5) * geometry.CellSize, (y + .5) * geometry.CellSize, (z + .5) * geometry.CellSize);
                if (graphs[i].StickyDistance is { } distance && !stickyQuery!.NearSurface(center, distance))
                { rejected.Add(cell); continue; }
                var reachable = ReachesSeed(center, cell, seeds[i], query, token);
                if (reachable) { accepted.Add(cell); additions.Add(cell); } else rejected.Add(cell);
            }
            results[i] = new(Array.AsReadOnly(accepted.ToArray()), Array.AsReadOnly(rejected.ToArray()));
        }
        var coverage = UyaOcclusionCoverage.Evaluate(primary, shapes, token, "uya-graph-coverage-v3\0"u8, additions.ToArray());
        token.ThrowIfCancellationRequested();
        coverage = coverage with { CandidateTests = checked(coverage.CandidateTests + (int)candidates) };
        progress?.Invoke(Math.Max(1, (int)candidates), Math.Max(1, (int)candidates));
        token.ThrowIfCancellationRequested();
        return new(coverage, Array.AsReadOnly(results), query.Rays, query.Tests);
    }

    private static bool ReachesSeed(D3 center, OcclusionCoverageCoordinate cell, D3[] seeds,
        UyaOcclusionCoverageQuery query, CancellationToken token)
    {
        // Try the nearest seed first; acceptance is still the union of all seed/probe connections.
        var nearest = 0;
        var nearestDistance = double.PositiveInfinity;
        for (var i = 0; i < seeds.Length; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            var delta = seeds[i] - center;
            var distance = delta.Dot(delta);
            if (distance < nearestDistance) { nearest = i; nearestDistance = distance; }
        }
        var size = UyaOcclusionCoverage.Profile.CellSize;
        var inset = Profile.ProbeInset;
        for (var i = 0; i < seeds.Length; i++)
        {
            var seed = seeds[i == 0 ? nearest : i <= nearest ? i - 1 : i];
            for (var probe = 0; probe < 9; probe++)
            {
                var point = probe == 0 ? center
                    : new D3(cell.X * (double)size + (((probe - 1) & 1) == 0 ? inset : size - inset),
                        cell.Y * (double)size + (((probe - 1) & 2) == 0 ? inset : size - inset),
                        cell.Z * (double)size + (((probe - 1) & 4) == 0 ? inset : size - inset));
                if (!query.Blocked(point, seed)) return true;
            }
        }
        return false;
    }
}
