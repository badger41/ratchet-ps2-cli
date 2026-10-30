using RatchetPs2.Core.Occlusion;
using System.Numerics;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Double-precision, brute-force oracle for bounded finite-ray fixtures.</summary>
public static class UyaOcclusionRayReference
{
    public static IReadOnlyList<byte[]> Trace(
        IReadOnlyList<OcclusionSurfaceTriangle> triangles,
        IReadOnlyList<(Vector3 Origin, Vector3 Direction)> rays,
        CancellationToken cancellationToken = default)
        => Trace(triangles, rays, [], cancellationToken);

    public static IReadOnlyList<byte[]> Trace(
        IReadOnlyList<OcclusionSurfaceTriangle> triangles,
        IReadOnlyList<(Vector3 Origin, Vector3 Direction)> rays,
        IReadOnlyList<OcclusionAlphaTexture> textures,
        CancellationToken cancellationToken = default)
        => TraceCore(triangles, rays, textures, [], resolveDepthOwnership: false, cancellationToken).Select(result => result.Geometry).ToArray();

    public static IReadOnlyList<OcclusionReferenceMasks> TraceCalibrated(
        IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<(Vector3 Origin, Vector3 Direction)> rays,
        IReadOnlyList<OcclusionAlphaTexture> textures, IReadOnlyList<OcclusionTargetSphere> spheres,
        CancellationToken token = default, float backfaceExitDistance = 0, IReadOnlyList<float>? barrierHeights = null)
        => TraceCore(triangles, rays, textures, spheres, resolveDepthOwnership: true, token,
            backfaceExitDistance: backfaceExitDistance, barrierHeights: barrierHeights);

    private static IReadOnlyList<OcclusionReferenceMasks> TraceCore(
        IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<(Vector3 Origin, Vector3 Direction)> rays,
        IReadOnlyList<OcclusionAlphaTexture> textures, IReadOnlyList<OcclusionTargetSphere> spheres,
        bool resolveDepthOwnership, CancellationToken cancellationToken,
        float backfaceExitDistance = 0, IReadOnlyList<float>? barrierHeights = null)
    {
        const float farDistance = UyaOcclusionLimits.FarDistance;
        const float tolerance = UyaOcclusionLimits.DepthTolerance;
        if (!float.IsFinite(backfaceExitDistance) || backfaceExitDistance is < 0 or > 1000)
            throw new InvalidDataException("Invalid nearby-backface exit distance.");
        if (barrierHeights is not null && (barrierHeights.Count>16 || barrierHeights.Any(h=>!float.IsFinite(h) || Math.Abs(h)>1000000)
            || barrierHeights.Distinct().Count()!=barrierHeights.Count))
            throw new InvalidDataException("Invalid occlusion barrier heights.");
        if (spheres.Count > 1024 || spheres.Any(s => s.Center.Length != 3 || s.Center.Any(v => !float.IsFinite(v))
            || !float.IsFinite(s.Radius) || s.Radius <= 0 || s.Group is < 0 or > 1023)) throw new InvalidDataException("Invalid occlusion target spheres.");
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(rays);
        cancellationToken.ThrowIfCancellationRequested();
        if (triangles.Count > UyaOcclusionLimits.MaximumTriangles
            || rays.Count > 256 || (long)triangles.Count * rays.Count * 2 > UyaOcclusionLimits.MaximumCpuRayTests)
            throw new InvalidDataException("CPU occlusion reference exceeds its bounded ray/triangle work limit.");
        // Validate independently of the BVH; this oracle deliberately does not walk the accelerated hierarchy.
        foreach (var triangle in triangles)
            if (triangle.Group is < -1 or >= 1024 || triangle.Flags > 3
                || !Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C))
                throw new InvalidDataException("CPU occlusion reference triangle is invalid.");
        UyaOcclusionAlphaSampling.Validate(triangles, textures);
        var results = new List<OcclusionReferenceMasks>(rays.Count);
        foreach (var (origin, direction) in rays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Finite(origin) || !Finite(direction) || Math.Abs(direction.LengthSquared() - 1) > .001)
                throw new InvalidDataException("CPU reference rays require finite origins and normalized directions.");
            var barrier = (double)farDistance;
            if (barrierHeights is not null) foreach (var height in barrierHeights)
            {
                var delta=(double)height-origin.Z;
                if (delta*direction.Z>0) barrier=Math.Min(barrier,delta/direction.Z);
            }
            var nearest = barrier;
            var owner = -1;
            for (var index = 0; index < triangles.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var triangle = triangles[index];
                if ((triangle.Flags & 1) == 0) continue;
                var distance = Hit(origin, direction, triangle, out var front, out var u, out var v);
                if ((!front && ((triangle.Flags & 2) == 0 || distance <= backfaceExitDistance)) || distance < UyaOcclusionLimits.NearDistance || distance > farDistance
                    || UyaOcclusionAlphaSampling.Sample(triangle, u, v, textures) < 127) continue;
                var rank = triangle.SourceIndex >= 0 ? triangle.SourceIndex : index;
                if (distance < nearest || distance == nearest && (owner < 0 || resolveDepthOwnership && rank < owner))
                { nearest = distance; owner = rank; }
            }
            var mask = new byte[128];
            for (var index = 0; index < triangles.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var triangle = triangles[index];
                var distance = Hit(origin, direction, triangle, out _, out var u, out var v);
                var opacity = UyaOcclusionAlphaSampling.Sample(triangle, u, v, textures);
                var rank = triangle.SourceIndex >= 0 ? triangle.SourceIndex : index;
                if (resolveDepthOwnership && distance == nearest && opacity >= 127 && (triangle.Flags & 1) != 0 && rank != owner) continue;
                if (triangle.Group >= 0 && distance >= UyaOcclusionLimits.NearDistance && distance <= nearest + tolerance
                    && distance <= farDistance && distance <= barrier + .0001
                    && opacity > 0)
                    mask[triangle.Group / 8] |= (byte)(1 << (triangle.Group & 7));
            }
            var mobyMask = new byte[128];
            foreach (var sphere in spheres)
            {
                double x = (double)origin.X - sphere.Center[0], y = (double)origin.Y - sphere.Center[1], z = (double)origin.Z - sphere.Center[2];
                var b = x * direction.X + y * direction.Y + z * direction.Z; var c = x * x + y * y + z * z - (double)sphere.Radius * sphere.Radius;
                var discriminant = b * b - c; if (discriminant < 0) continue;
                var entry = -b - Math.Sqrt(discriminant); var exit = -b + Math.Sqrt(discriminant);
                var distance = Math.Max(.01, entry);
                if (exit >= .01 && distance <= farDistance && distance <= nearest + tolerance && distance <= barrier + .0001) mobyMask[sphere.Group / 8] |= (byte)(1 << (sphere.Group & 7));
            }
            results.Add(new(mask, mobyMask));
        }
        return results;
    }

    private static double Hit(Vector3 origin, Vector3 direction, OcclusionSurfaceTriangle triangle, out bool front, out double u, out double v)
    {
        u = 0; v = 0;
        double e1x = (double)triangle.B.X - triangle.A.X, e1y = (double)triangle.B.Y - triangle.A.Y, e1z = (double)triangle.B.Z - triangle.A.Z;
        double e2x = (double)triangle.C.X - triangle.A.X, e2y = (double)triangle.C.Y - triangle.A.Y, e2z = (double)triangle.C.Z - triangle.A.Z;
        var px = direction.Y * e2z - direction.Z * e2y;
        var py = direction.Z * e2x - direction.X * e2z;
        var pz = direction.X * e2y - direction.Y * e2x;
        var determinant = e1x * px + e1y * py + e1z * pz;
        front = determinant > 0;
        if (Math.Abs(determinant) < 1e-8) return double.PositiveInfinity;
        var tx = (double)origin.X - triangle.A.X; var ty = (double)origin.Y - triangle.A.Y; var tz = (double)origin.Z - triangle.A.Z;
        u = (tx * px + ty * py + tz * pz) / determinant;
        if (u < -1e-5 || u > 1.00001) return double.PositiveInfinity;
        var qx = ty * e1z - tz * e1y; var qy = tz * e1x - tx * e1z; var qz = tx * e1y - ty * e1x;
        v = (direction.X * qx + direction.Y * qy + direction.Z * qz) / determinant;
        if (v < -1e-5 || u + v > 1.00001) return double.PositiveInfinity;
        return (e2x * qx + e2y * qy + e2z * qz) / determinant;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
