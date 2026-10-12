using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaTfragCollisionSimplifier
{
    internal const float DefaultTargetFaceRatio = 0.5f;
    private const float MinimumTargetFaceRatio = 0.1f;
    private const float MaximumTargetFaceRatio = 0.95f;
    private const float MinimumDeviation = 1f / 16f;
    private const float MaximumDeviation = 0.35f;
    private const float DeviationBoundsRatio = 0.01f;
    private const float MinimumNormalDot = 0.70710678f;
    private const int MaximumSourceTriangles = 256;

    internal static Result Simplify(
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<(int A, int B, int C)> sourceTriangles,
        float targetFaceRatio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePositions);
        ArgumentNullException.ThrowIfNull(sourceTriangles);
        if (sourceTriangles.Count > MaximumSourceTriangles)
            throw new InvalidDataException(
                $"Tfrag collision decimation supports at most {MaximumSourceTriangles} source triangles per piece.");
        if (!float.IsFinite(targetFaceRatio)
            || targetFaceRatio < MinimumTargetFaceRatio
            || targetFaceRatio > MaximumTargetFaceRatio)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFaceRatio),
                $"Tfrag collision target ratio must be between {MinimumTargetFaceRatio} and {MaximumTargetFaceRatio}.");
        }

        var mesh = Weld(sourcePositions, sourceTriangles, cancellationToken);
        var targetFaces = Math.Max(1, (int)MathF.Ceiling(mesh.Triangles.Count * targetFaceRatio));
        var sourceSurface = Triangles(sourcePositions, sourceTriangles);
        var allowedDeviation = AllowedDeviation(mesh.Positions);
        var constraints = TopologyConstraints(mesh.Triangles);
        var triangles = mesh.Triangles.ToList();
        var maximumDeviation = MaximumDeviationFromSource(
            sourceSurface, mesh.Positions, triangles, cancellationToken);

        while (triangles.Count > targetFaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edges = BuildEdges(triangles);
            Collapse? accepted = null;
            foreach (var edge in edges
                .Where(pair => CollapsibleEdge(pair.Key, pair.Value, constraints)
                    && SmoothEdge(mesh.Positions, triangles, pair.Value))
                .OrderBy(pair => Vector3.DistanceSquared(
                    mesh.Positions[pair.Key.A], mesh.Positions[pair.Key.B])))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SatisfiesLinkCondition(edge.Key, triangles, edge.Value)) continue;
                var left = TryCollapse(edge.Key.B, edge.Key.A, mesh.Positions, triangles,
                    sourceSurface, allowedDeviation, cancellationToken);
                var right = TryCollapse(edge.Key.A, edge.Key.B, mesh.Positions, triangles,
                    sourceSurface, allowedDeviation, cancellationToken);
                accepted = left is null ? right
                    : right is null || left.MaximumDeviation <= right.MaximumDeviation ? left : right;
                if (accepted is not null) break;
            }

            if (accepted is null) break;
            triangles = accepted.Triangles;
            maximumDeviation = MathF.Max(maximumDeviation, accepted.MaximumDeviation);
        }

        return new(mesh.Positions, triangles, maximumDeviation);
    }

    private static Mesh Weld(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        CancellationToken cancellationToken)
    {
        var weldedPositions = new List<Vector3>();
        var indexByVertex = new Dictionary<UyaCollisionVertex, int>();
        var remap = new int[positions.Count];
        for (var index = 0; index < positions.Count; index++)
        {
            var vertex = UyaInstancedCollisionSurfaceGenerator.Quantize(positions[index]);
            if (!indexByVertex.TryGetValue(vertex, out var weldedIndex))
            {
                weldedIndex = weldedPositions.Count;
                indexByVertex.Add(vertex, weldedIndex);
                weldedPositions.Add(vertex.Position);
            }
            remap[index] = weldedIndex;
        }

        var weldedTriangles = new List<(int A, int B, int C)>();
        var keys = new HashSet<TriangleKey>();
        foreach (var triangle in triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = (A: remap[triangle.A], B: remap[triangle.B], C: remap[triangle.C]);
            if (value.A == value.B || value.A == value.C || value.B == value.C) continue;
            if (keys.Add(TriangleKey.Create(value.A, value.B, value.C))) weldedTriangles.Add(value);
        }
        return new(weldedPositions, weldedTriangles);
    }

    private static Constraints TopologyConstraints(
        IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var locked = new HashSet<int>();
        var boundary = new HashSet<int>();
        var edges = BuildEdges(triangles);
        foreach (var pair in edges)
        {
            if (pair.Value.Count == 1)
            {
                boundary.Add(pair.Key.A);
                boundary.Add(pair.Key.B);
            }
            else if (pair.Value.Count != 2)
            {
                locked.Add(pair.Key.A);
                locked.Add(pair.Key.B);
            }
        }
        return new(locked, boundary);
    }

    private static bool CollapsibleEdge(EdgeKey edge, IReadOnlyList<int> triangles, Constraints constraints)
    {
        if (triangles.Count is < 1 or > 2
            || constraints.Locked.Contains(edge.A)
            || constraints.Locked.Contains(edge.B)) return false;
        var boundaryA = constraints.Boundary.Contains(edge.A);
        var boundaryB = constraints.Boundary.Contains(edge.B);
        return triangles.Count == 1 ? boundaryA && boundaryB : !boundaryA && !boundaryB;
    }

    private static bool SmoothEdge(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        IReadOnlyList<int> edgeTriangles) => edgeTriangles.Count == 1
        || edgeTriangles.Count == 2 && Vector3.Dot(
            Normal(positions, triangles[edgeTriangles[0]]),
            Normal(positions, triangles[edgeTriangles[1]])) >= MinimumNormalDot;

    private static Collapse? TryCollapse(
        int remove,
        int keep,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        IReadOnlyList<SurfaceTriangle> sourceSurface,
        float allowedDeviation,
        CancellationToken cancellationToken)
    {
        var result = new List<(int A, int B, int C)>(triangles.Count - 2);
        var keys = new HashSet<TriangleKey>();
        foreach (var triangle in triangles)
        {
            var mapped = (
                A: triangle.A == remove ? keep : triangle.A,
                B: triangle.B == remove ? keep : triangle.B,
                C: triangle.C == remove ? keep : triangle.C);
            if (mapped.A == mapped.B || mapped.A == mapped.C || mapped.B == mapped.C) continue;
            if (triangle.A == remove || triangle.B == remove || triangle.C == remove)
            {
                var before = Normal(positions, triangle);
                var after = Normal(positions, mapped);
                if (after == Vector3.Zero || Vector3.Dot(before, after) < MinimumNormalDot) return null;
            }
            if (keys.Add(TriangleKey.Create(mapped.A, mapped.B, mapped.C))) result.Add(mapped);
        }
        if (result.Count >= triangles.Count) return null;
        var deviation = MaximumDeviationFromSource(sourceSurface, positions, result, cancellationToken);
        return deviation <= allowedDeviation ? new(result, deviation) : null;
    }

    private static bool SatisfiesLinkCondition(
        EdgeKey edge,
        IReadOnlyList<(int A, int B, int C)> triangles,
        IReadOnlyList<int> edgeTriangles)
    {
        var leftNeighbors = Neighbors(edge.A, triangles);
        var rightNeighbors = Neighbors(edge.B, triangles);
        leftNeighbors.IntersectWith(rightNeighbors);
        var opposites = edgeTriangles.Select(index => Opposite(triangles[index], edge)).ToHashSet();
        return leftNeighbors.SetEquals(opposites);
    }

    private static HashSet<int> Neighbors(int vertex, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var result = new HashSet<int>();
        foreach (var triangle in triangles)
        {
            if (triangle.A != vertex && triangle.B != vertex && triangle.C != vertex) continue;
            if (triangle.A != vertex) result.Add(triangle.A);
            if (triangle.B != vertex) result.Add(triangle.B);
            if (triangle.C != vertex) result.Add(triangle.C);
        }
        return result;
    }

    private static int Opposite((int A, int B, int C) triangle, EdgeKey edge)
        => triangle.A != edge.A && triangle.A != edge.B ? triangle.A
            : triangle.B != edge.A && triangle.B != edge.B ? triangle.B : triangle.C;

    private static Dictionary<EdgeKey, List<int>> BuildEdges(IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var result = new Dictionary<EdgeKey, List<int>>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            Add(EdgeKey.Create(triangle.A, triangle.B), index);
            Add(EdgeKey.Create(triangle.B, triangle.C), index);
            Add(EdgeKey.Create(triangle.C, triangle.A), index);
        }
        return result;

        void Add(EdgeKey edge, int triangle)
        {
            if (!result.TryGetValue(edge, out var indexes)) result.Add(edge, indexes = []);
            indexes.Add(triangle);
        }
    }

    private static float AllowedDeviation(IReadOnlyList<Vector3> positions)
    {
        var minimum = positions[0];
        var maximum = positions[0];
        foreach (var position in positions)
        {
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }
        return Math.Clamp(Vector3.Distance(minimum, maximum) * DeviationBoundsRatio,
            MinimumDeviation, MaximumDeviation);
    }

    private static float MaximumDeviationFromSource(
        IReadOnlyList<SurfaceTriangle> source,
        IReadOnlyList<Vector3> targetPositions,
        IReadOnlyList<(int A, int B, int C)> targetTriangles,
        CancellationToken cancellationToken)
    {
        var target = Triangles(targetPositions, targetTriangles);
        if (source.Count == 0 || target.Count == 0) return float.PositiveInfinity;
        var maximumSquared = 0f;
        foreach (var sample in source.SelectMany(Samples))
        {
            cancellationToken.ThrowIfCancellationRequested();
            maximumSquared = MathF.Max(maximumSquared,
                target.Min(triangle => PointTriangleDistanceSquared(sample, triangle)));
        }
        foreach (var sample in target.SelectMany(Samples))
        {
            cancellationToken.ThrowIfCancellationRequested();
            maximumSquared = MathF.Max(maximumSquared,
                source.Min(triangle => PointTriangleDistanceSquared(sample, triangle)));
        }
        return MathF.Sqrt(maximumSquared);
    }

    private static List<SurfaceTriangle> Triangles(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles) => triangles
        .Select(value => new SurfaceTriangle(positions[value.A], positions[value.B], positions[value.C]))
        .ToList();

    private static IEnumerable<Vector3> Samples(SurfaceTriangle triangle)
    {
        yield return triangle.A;
        yield return triangle.B;
        yield return triangle.C;
        yield return (triangle.A + triangle.B) * 0.5f;
        yield return (triangle.B + triangle.C) * 0.5f;
        yield return (triangle.C + triangle.A) * 0.5f;
        yield return (triangle.A + triangle.B + triangle.C) / 3f;
    }

    private static Vector3 Normal(
        IReadOnlyList<Vector3> positions,
        (int A, int B, int C) triangle)
    {
        var normal = Vector3.Cross(
            positions[triangle.B] - positions[triangle.A],
            positions[triangle.C] - positions[triangle.A]);
        return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero;
    }

    private static float PointTriangleDistanceSquared(Vector3 point, SurfaceTriangle triangle)
    {
        var ab = triangle.B - triangle.A;
        var ac = triangle.C - triangle.A;
        var ap = point - triangle.A;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();
        var bp = point - triangle.B;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return Vector3.DistanceSquared(point, triangle.A + ab * (d1 / (d1 - d3)));
        var cp = point - triangle.C;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return Vector3.DistanceSquared(point, triangle.A + ac * (d2 / (d2 - d6)));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return Vector3.DistanceSquared(point,
                triangle.B + (triangle.C - triangle.B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        var denominator = 1f / (va + vb + vc);
        return Vector3.DistanceSquared(point,
            triangle.A + ab * (vb * denominator) + ac * (vc * denominator));
    }

    internal sealed record Result(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<(int A, int B, int C)> Triangles,
        float MaximumDeviation);

    private sealed record Mesh(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<(int A, int B, int C)> Triangles);

    private sealed record Collapse(List<(int A, int B, int C)> Triangles, float MaximumDeviation);
    private sealed record Constraints(HashSet<int> Locked, HashSet<int> Boundary);
    private readonly record struct SurfaceTriangle(Vector3 A, Vector3 B, Vector3 C);
    private readonly record struct EdgeKey(int A, int B)
    {
        public static EdgeKey Create(int a, int b) => a <= b ? new(a, b) : new(b, a);
    }
    private readonly record struct TriangleKey(int A, int B, int C)
    {
        public static TriangleKey Create(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return new(a, b, c);
        }
    }
}
