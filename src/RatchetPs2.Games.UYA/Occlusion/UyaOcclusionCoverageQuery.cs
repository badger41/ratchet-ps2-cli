using System.Buffers;
using RatchetPs2.Core.Occlusion;
using static RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Bounded finite-segment queries; uncertain intersections never remove coverage.</summary>
internal sealed class UyaOcclusionCoverageQuery
{
    private readonly IReadOnlyList<OcclusionSurfaceTriangle> triangles;
    private readonly IReadOnlyList<OcclusionAlphaTexture> textures;
    private readonly int[] order;
    private readonly List<Node> nodes = [];
    private readonly CancellationToken token;
    private readonly Budget budget;
    private int lastBlocker = -1;
    private int lastSurface = -1;
    internal Budget WorkBudget => budget;
    internal int Tests => budget.Tests;
    internal int Rays => budget.Rays;
    internal sealed class Budget { internal int Tests; internal int Rays; }
    private static readonly D3[] StickyDirections = [new(1,1,1),new(1,1,-1),new(1,-1,1),new(1,-1,-1),
        new(-1,1,1),new(-1,1,-1),new(-1,-1,1),new(-1,-1,-1),new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)];

    private static readonly D3[] BuriedDirections = [new(1, .173, .397), new(-1, -.173, -.397), new(.291, 1, .113),
        new(-.291, -1, -.113), new(.137, .319, 1), new(-.137, -.319, -1)];

    internal UyaOcclusionCoverageQuery(IReadOnlyList<OcclusionSurfaceTriangle> triangles,
        IReadOnlyList<OcclusionAlphaTexture> textures, CancellationToken token, Budget? budget = null)
    {
        this.triangles = triangles; this.textures = textures; this.token = token; this.budget = budget ?? new();
        ArgumentNullException.ThrowIfNull(triangles); ArgumentNullException.ThrowIfNull(textures);
        if (triangles.Count > UyaOcclusionLimits.MaximumTriangles) throw new InvalidDataException("Graph scene exceeds the triangle limit.");
        foreach (var t in triangles)
        {
            token.ThrowIfCancellationRequested();
            if (t.Flags > 3 || t.Group is < -1 or > 1023 || !Finite(t.A) || !Finite(t.B) || !Finite(t.C))
                throw new InvalidDataException("Graph scene contains invalid triangle coordinates, flags or groups.");
        }
        if (textures.Count > UyaOcclusionLimits.MaximumTextures) throw new InvalidDataException("Graph scene exceeds its texture limit.");
        if (textures.Any(t => t is null || t.Alpha is null)) throw new InvalidDataException("Graph alpha textures are missing.");
        UyaOcclusionAlphaSampling.Validate(triangles, textures);
        order = Enumerable.Range(0, triangles.Count).Where(i => (triangles[i].Flags & 1) != 0).ToArray();
        if (order.Length > 0)
        {
            var centers = ArrayPool<double>.Shared.Rent(triangles.Count);
            try { Build(0, order.Length, centers); }
            finally { ArrayPool<double>.Shared.Return(centers); }
        }
    }

    private int Build(int start, int count, double[] centers)
    {
        token.ThrowIfCancellationRequested();
        var min = new D3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new D3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (var i = start; i < start + count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var t = triangles[order[i]];
            min = Min(min, Min(From(t.A), Min(From(t.B), From(t.C))));
            max = Max(max, Max(From(t.A), Max(From(t.B), From(t.C))));
        }
        var index = nodes.Count; nodes.Add(new(min, max, start, count, -1, -1));
        if (count <= 8) return index;
        var size = max - min; var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        // Sort by cached centroids instead of repeatedly copying triangle records in the comparer.
        for (var i = start; i < start + count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var face = order[i];
            var t = triangles[face];
            centers[face] = (Axis(From(t.A), axis) + Axis(From(t.B), axis) + Axis(From(t.C), axis)) / 3;
        }
        var comparisons = 0;
        try
        {
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
            {
                if ((++comparisons & 1023) == 0) token.ThrowIfCancellationRequested();
                var c = centers[a].CompareTo(centers[b]); return c != 0 ? c : a.CompareTo(b);
            }));
        }
        catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException)
        { token.ThrowIfCancellationRequested(); throw; }
        token.ThrowIfCancellationRequested();
        var left = Build(start, count / 2, centers); var right = Build(start + count / 2, count - count / 2, centers);
        nodes[index] = new(min, max, start, 0, left, right); return index;
    }

    internal bool Blocked(D3 origin, D3 target) => Trace(origin, target, anyHit: true) is not null;

    internal bool NearSurface(D3 point, double distance)
    {
        foreach (var d in StickyDirections)
        {
            // Match v1: start two units behind the octant center on each unnormalized axis/corner direction.
            var target = point + d * (distance / Math.Sqrt(d.Dot(d)));
            if (Trace(point - d * 2, target, anyHit: true, surface: true) is not null) return true;
        }
        return false;
    }

    internal bool Buried(D3 point)
    {
        // Only diagnose six agreeing, definite backface exits; open or uncertain geometry remains usable.
        foreach (var d in BuriedDirections)
        {
            var hit = Trace(point, point + d * 4_000_000, anyHit: false);
            if (hit is null || hit.Value.Front) return false;
        }
        return true;
    }

    private Hit? Trace(D3 origin, D3 target, bool anyHit, bool surface = false)
    {
        token.ThrowIfCancellationRequested();
        if (++budget.Rays > UyaOcclusionGraphCoverage.Profile.MaximumRays)
            throw new InvalidDataException("Graph preview exceeds its ray limit; reduce regions or seeds.");
        var delta = target - origin; var length = Math.Sqrt(delta.Dot(delta));
        var clearance = surface ? 0 : UyaOcclusionGraphCoverage.Profile.EndpointClearance;
        if (length <= 2 * clearance || nodes.Count == 0) return null;
        var direction = delta * (1 / length);
        // Adjacent probes often hit the same face; recheck it exactly before traversing the index.
        var cached = surface ? lastSurface : lastBlocker;
        if (anyHit && cached >= 0)
        {
            Work();
            var hit = Intersect(cached, origin, direction, length, clearance, surface, double.PositiveInfinity);
            if (hit is not null) return hit;
        }
        Hit? nearest = null;
        void Visit(int index)
        {
            if (anyHit && nearest is not null) return;
            Work(); var node = nodes[index];
            var end = nearest?.Distance ?? length - clearance;
            if (!Touches(origin, direction, node.Min, node.Max, clearance, end)) return;
            if (node.Count == 0) { Visit(node.Left); Visit(node.Right); return; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                Work();
                var face = order[i];
                var hit = Intersect(face, origin, direction, length, clearance, surface,
                    (nearest?.Distance ?? double.PositiveInfinity) + 1e-8);
                if (hit is null) continue;
                if (nearest is { } previous && Math.Abs(hit.Value.Distance - previous.Distance) <= 1e-8)
                    nearest = new(Math.Min(hit.Value.Distance, previous.Distance), previous.Front || hit.Value.Front);
                else nearest = hit;
                if (anyHit)
                {
                    if (surface) lastSurface = face; else lastBlocker = face;
                    return;
                }
            }
        }
        Visit(0); return nearest;
    }

    private Hit? Intersect(int index, D3 origin, D3 direction, double length, double clearance,
        bool surface, double maximumDistance)
    {
        var triangle = triangles[index];
        var a = From(triangle.A); var e = From(triangle.B) - a; var f = From(triangle.C) - a;
        var cross = direction.Cross(f); var determinant = e.Dot(cross);
        if (Math.Abs(determinant) <= 1e-12 * Math.Max(1, Math.Sqrt(e.Dot(e) * f.Dot(f)))) return null;
        if (surface && determinant <= 0) return null;
        var offset = origin - a; var u = offset.Dot(cross) / determinant;
        var q = offset.Cross(e); var v = direction.Dot(q) / determinant;
        // Edge, vertex and endpoint contacts are uncertain and therefore do not block.
        if (surface ? u < -1e-8 || v < -1e-8 || u + v > 1 + 1e-8
            : u <= 1e-8 || v <= 1e-8 || u + v >= 1 - 1e-8) return null;
        var distance = f.Dot(q) / determinant;
        if ((surface ? distance < 0 || distance > length : distance <= clearance || distance >= length - clearance)
            || distance > maximumDistance) return null;
        if (UyaOcclusionAlphaSampling.Sample(triangle, u, v, textures) <= UyaOcclusionLimits.AlphaThreshold + .00001) return null;
        return new(distance, determinant > 0);
    }

    private void Work()
    {
        if ((++budget.Tests & 255) == 0) token.ThrowIfCancellationRequested();
        if (Tests > UyaOcclusionGraphCoverage.Profile.MaximumQueryTests)
            throw new InvalidDataException(FormattableString.Invariant($"Graph preview exceeds its scene-query work limit ({UyaOcclusionGraphCoverage.Profile.MaximumQueryTests:N0} geometry tests after {Rays:N0} rays; {triangles.Count:N0} scene triangles). Reduce the total enabled graph region size, seeds or blocker geometry."));
    }

    private static bool Touches(D3 origin, D3 direction, D3 min, D3 max, double near, double far)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            var o = Axis(origin, axis); var d = Axis(direction, axis); var lo = Axis(min, axis) - .001; var hi = Axis(max, axis) + .001;
            if (Math.Abs(d) < 1e-15) { if (o < lo || o > hi) return false; continue; }
            var a = (lo - o) / d; var b = (hi - o) / d;
            near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
            if (near > far) return false;
        }
        return true;
    }

    private static bool Finite(System.Numerics.Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && Math.Abs(p.X) <= 1_000_000 && Math.Abs(p.Y) <= 1_000_000 && Math.Abs(p.Z) <= 1_000_000;
    internal static D3 From(System.Numerics.Vector3 p) => new(p.X, p.Y, p.Z);
    private static double Axis(D3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
    private static D3 Min(D3 a, D3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static D3 Max(D3 a, D3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    private sealed record Node(D3 Min, D3 Max, int Start, int Count, int Left, int Right);
    private readonly record struct Hit(double Distance, bool Front);
}
