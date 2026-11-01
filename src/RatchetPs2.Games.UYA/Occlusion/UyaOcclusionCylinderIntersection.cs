using static RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry;

namespace RatchetPs2.Games.UYA.Occlusion;

internal static class UyaOcclusionCylinderIntersection
{
    internal static bool Intersects(D3 localCenter, D3[] offsets, double radius, double halfHeight)
    {
        var reach = radius + Math.Sqrt(12);
        if (localCenter.X * localCenter.X + localCenter.Y * localCenter.Y > reach * reach) return false;
        Span<D3> corners = stackalloc D3[8];
        Span<D2> points = stackalloc D2[32]; var count = 0;
        for (var i = 0; i < 8; i++)
        {
            var p = localCenter + offsets[i]; corners[i] = p;
            if (Math.Abs(p.Z) <= halfHeight) points[count++] = new(p.X, p.Y);
        }
        // Clipping a cube by two cap planes adds vertices only on its twelve original edges.
        for (var i = 0; i < 8; i++) for (var axis = 0; axis < 3; axis++)
        {
            var j = i ^ (1 << axis); if (j < i) continue;
            var a = corners[i]; var b = corners[j]; var dz = b.Z - a.Z;
            if (dz == 0) continue;
            for (var side = -1; side <= 1; side += 2)
            {
                var cap = side * halfHeight;
                if (cap < Math.Min(a.Z, b.Z) || cap > Math.Max(a.Z, b.Z)) continue;
                var t = (cap - a.Z) / dz;
                points[count++] = new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }
        if (count == 0) return false;
        // The clipped solid intersects the circular cylinder iff its radial projection intersects the disk.
        var polygon = points[..count];
        for (var i = 1; i < polygon.Length; i++)
        {
            var value = polygon[i]; var j = i - 1;
            while (j >= 0 && Compare(polygon[j], value) > 0) { polygon[j + 1] = polygon[j]; j--; }
            polygon[j + 1] = value;
        }
        var unique = 0;
        for (var i = 0; i < polygon.Length; i++)
            if (unique == 0 || polygon[i] != polygon[unique - 1]) polygon[unique++] = polygon[i];
        polygon = polygon[..unique];
        Span<D2> hull = stackalloc D2[64]; var length = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            while (length >= 2 && Cross(hull[length - 2], hull[length - 1], polygon[i]) <= 0) length--;
            hull[length++] = polygon[i];
        }
        var lower = length + 1;
        for (var i = polygon.Length - 2; i >= 0; i--)
        {
            while (length >= lower && Cross(hull[length - 2], hull[length - 1], polygon[i]) <= 0) length--;
            hull[length++] = polygon[i];
        }
        if (length > 1) length--;
        var radiusSquared = radius * radius; var inside = length >= 3;
        for (var i = 0; i < length; i++)
        {
            var a = hull[i]; var b = hull[(i + 1) % length];
            if (Cross(a, b, default) < 0) inside = false;
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var distance = dx * dx + dy * dy;
            var t = distance > 0 ? Math.Clamp(-(a.X * dx + a.Y * dy) / distance, 0, 1) : 0;
            var x = a.X + t * dx; var y = a.Y + t * dy;
            if (x * x + y * y <= radiusSquared) return true;
        }
        return inside;
    }

    private static int Compare(D2 a, D2 b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
    private static double Cross(D2 a, D2 b, D2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private readonly record struct D2(double X, double Y);
}
