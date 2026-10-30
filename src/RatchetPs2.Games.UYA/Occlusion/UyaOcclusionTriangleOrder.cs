using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using System.Numerics;

namespace RatchetPs2.Games.UYA.Occlusion;

// Preserve the calibrated spatial order for conservative raster batches, without constructing a ray BVH.
internal static class UyaOcclusionTriangleOrder
{
    internal static IReadOnlyList<OcclusionSurfaceTriangle> Sort(
        IReadOnlyList<OcclusionSurfaceTriangle> source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Count > UyaOcclusionLimits.MaximumTriangles)
            throw new InvalidDataException("Occlusion scene exceeds its triangle limit.");
        var triangles = source.Select((triangle, index) => (Triangle: triangle, Index: index)).ToArray();
        foreach (var (triangle, _) in triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (triangle.Group is < -1 or >= 1024 || triangle.Flags > 3
                || triangle.TextureIndex < -1 || triangle.WrapFlags > 3
                || !FiniteUv(triangle.UvA) || !FiniteUv(triangle.UvB) || !FiniteUv(triangle.UvC)
                || !Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C))
                throw new InvalidDataException("Occlusion scene triangle has invalid coordinates, group, or flags.");
        }
        if (triangles.Length == 0) return [];
        SortRange(0, triangles.Length, 0);
        return triangles.Select(value => value.Triangle).ToArray();

        void SortRange(int start, int count, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth >= 60) throw new InvalidDataException("Occlusion spatial ordering exceeds its depth limit.");
            var minimum = new Vector3(float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity);
            for (var index = start; index < start + count; index++)
            {
                var t = triangles[index].Triangle;
                minimum = Vector3.Min(minimum, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                maximum = Vector3.Max(maximum, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
            }
            if (count <= 8) return;
            var extent = maximum - minimum;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(triangles, start, count, Comparer<(OcclusionSurfaceTriangle Triangle, int Index)>.Create((left, right) =>
            {
                var a = left.Triangle; var b = right.Triangle;
                var ca = (a.A[axis] + a.B[axis] + a.C[axis]) / 3f;
                var cb = (b.A[axis] + b.B[axis] + b.C[axis]) / 3f;
                var order = ca.CompareTo(cb);
                return order == 0 ? left.Index.CompareTo(right.Index) : order;
            }));
            var half = count / 2;
            SortRange(start, half, depth + 1);
            SortRange(start + half, count - half, depth + 1);
        }
    }

    private static bool FiniteUv(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && Math.Abs(value.X) <= 1_000_000 && Math.Abs(value.Y) <= 1_000_000;

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && Math.Abs(value.X) <= 1_000_000 && Math.Abs(value.Y) <= 1_000_000 && Math.Abs(value.Z) <= 1_000_000;
}
