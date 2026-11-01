using System.Numerics;
using RatchetPs2.Core.Occlusion;

namespace RatchetPs2.Games.UYA.Occlusion;

internal static class UyaOcclusionCoverageGeometry
{
    private static OcclusionCoverageProfile Profile => UyaOcclusionCoverage.Profile;

    internal static PreparedShape Prepare(OcclusionCoverageShape input)
    {
        if (input is null || !Finite(input.Center))
            throw new InvalidDataException("UYA coverage shapes require finite centers.");
        var q = input.Rotation;
        double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.0001)
            throw new InvalidDataException("UYA coverage shape rotation must be a finite unit quaternion.");
        // Normalize in double precision so accepted near-unit poses remain orthonormal at large coordinates.
        var inverse = 1 / Math.Sqrt(norm);
        var x = q.X * inverse; var y = q.Y * inverse; var z = q.Z * inverse; var w = q.W * inverse;
        D3[] axes = [new(1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)),
            new(2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)),
            new(2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y))];
        var center = new D3(input.Center.X, input.Center.Y, input.Center.Z);
        if (input is OcclusionCoverageCylinder cylinder) return PrepareCylinder(cylinder, center, axes);
        if (input is not OcclusionCoverageBox box || !Finite(box.Size)
            || !Dimension(box.Size.X) || !Dimension(box.Size.Y) || !Dimension(box.Size.Z))
            throw new InvalidDataException("UYA coverage requires a box or cylinder with positive finite dimensions no greater than 1,000,000.");
        var half = new D3(box.Size.X / 2.0, box.Size.Y / 2.0, box.Size.Z / 2.0);
        var extent = axes[0].Abs() * half.X + axes[1].Abs() * half.Y + axes[2].Abs() * half.Z;
        var (minimum, maximum) = Bounds(center, extent);
        // Cache all 15 separating axes and projection radii; no per-candidate allocations or float transforms.
        D3[] world = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        var projections = world.Concat(axes).Concat(world.SelectMany(a => axes.Select(a.Cross)))
            .Where(a => a.Dot(a) > 1e-24).Select(a =>
            {
                var length = Math.Sqrt(a.Dot(a)); var unit = a * (1 / length);
                return new Projection(unit, Math.Abs(unit.Dot(axes[0])) * half.X
                    + Math.Abs(unit.Dot(axes[1])) * half.Y + Math.Abs(unit.Dot(axes[2])) * half.Z
                    + Profile.CellSize / 2 * (Math.Abs(unit.X) + Math.Abs(unit.Y) + Math.Abs(unit.Z)));
            }).ToArray();
        return new(center, half, axes, projections, minimum, maximum, input.Exclude, 0, []);
    }

    private static PreparedShape PrepareCylinder(OcclusionCoverageCylinder input, D3 center, D3[] axes)
    {
        if (!Dimension(input.Radius) || !Dimension(input.Height))
            throw new InvalidDataException("UYA coverage cylinders require a positive finite radius and height no greater than 1,000,000.");
        var radius = input.Radius + Profile.BoundaryTolerance;
        var halfHeight = input.Height / 2.0 + Profile.BoundaryTolerance;
        double Extent(double a, double b, double c) => radius * Math.Sqrt(a * a + b * b) + halfHeight * Math.Abs(c);
        var extent = new D3(Extent(axes[0].X, axes[1].X, axes[2].X), Extent(axes[0].Y, axes[1].Y, axes[2].Y),
            Extent(axes[0].Z, axes[1].Z, axes[2].Z));
        var (minimum, maximum) = Bounds(center, extent);
        var corners = new D3[8];
        for (var i = 0; i < 8; i++)
        {
            var corner = new D3((i & 1) == 0 ? -2 : 2, (i & 2) == 0 ? -2 : 2, (i & 4) == 0 ? -2 : 2);
            corners[i] = new(corner.Dot(axes[0]), corner.Dot(axes[1]), corner.Dot(axes[2]));
        }
        return new(center, new(0, 0, input.Height / 2.0), axes, [], minimum, maximum, input.Exclude, input.Radius, corners);
    }

    private static (OcclusionCoverageCoordinate, OcclusionCoverageCoordinate) Bounds(D3 center, D3 extent)
    {
        var low = center - extent; var high = center + extent;
        int Minimum(double value) => Bound(Math.Ceiling((value - Profile.BoundaryTolerance) / Profile.CellSize) - 1);
        int Maximum(double value) => Bound(Math.Floor((value + Profile.BoundaryTolerance) / Profile.CellSize));
        return (new(Minimum(low.X), Minimum(low.Y), Minimum(low.Z)), new(Maximum(high.X), Maximum(high.Y), Maximum(high.Z)));
    }

    private static int Bound(double value)
    {
        if (!double.IsFinite(value) || value < Profile.MinimumCoordinate || value > Profile.MaximumCoordinate)
            throw new InvalidDataException($"UYA coverage shape touches cells outside the structural preview coordinate range {Profile.MinimumCoordinate}–{Profile.MaximumCoordinate}; no cells were clipped.");
        return (int)value;
    }

    internal static void ValidateCoordinate(OcclusionCoverageCoordinate c)
    {
        if (c.X < Profile.MinimumCoordinate || c.Y < Profile.MinimumCoordinate || c.Z < Profile.MinimumCoordinate
            || c.X > Profile.MaximumCoordinate || c.Y > Profile.MaximumCoordinate || c.Z > Profile.MaximumCoordinate)
            throw new InvalidDataException("UYA Primary coverage contains coordinates outside the structural preview range.");
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Dimension(float value) => float.IsFinite(value) && value > 0 && value <= 1_000_000;

    internal sealed record PreparedShape(D3 Center, D3 Half, D3[] Axes, Projection[] Projections,
        OcclusionCoverageCoordinate Minimum, OcclusionCoverageCoordinate Maximum, bool Exclude, double Radius, D3[] CornerOffsets)
    {
        internal long CandidateCount => (long)(Maximum.X - Minimum.X + 1)
            * (Maximum.Y - Minimum.Y + 1) * (Maximum.Z - Minimum.Z + 1);

        private D3 Delta(OcclusionCoverageCoordinate c) => new D3(c.X * (double)Profile.CellSize + Profile.CellSize / 2,
            c.Y * (double)Profile.CellSize + Profile.CellSize / 2, c.Z * (double)Profile.CellSize + Profile.CellSize / 2) - Center;

        internal bool Intersects(OcclusionCoverageCoordinate c)
        {
            var delta = Delta(c);
            if (Radius > 0) return UyaOcclusionCylinderIntersection.Intersects(
                new(delta.Dot(Axes[0]), delta.Dot(Axes[1]), delta.Dot(Axes[2])), CornerOffsets,
                Radius + Profile.BoundaryTolerance, Half.Z + Profile.BoundaryTolerance);
            foreach (var p in Projections)
                if (Math.Abs(delta.Dot(p.Axis)) > p.Radius + Profile.BoundaryTolerance) return false;
            return true;
        }

        internal bool Contains(OcclusionCoverageCoordinate c)
        {
            var delta = Delta(c);
            if (Radius > 0)
            {
                var radius = Radius - Profile.BoundaryTolerance; var height = Half.Z - Profile.BoundaryTolerance;
                if (radius <= 0 || height <= 0) return false;
                var local = new D3(delta.Dot(Axes[0]), delta.Dot(Axes[1]), delta.Dot(Axes[2]));
                foreach (var offset in CornerOffsets)
                {
                    var p = local + offset;
                    if (Math.Abs(p.Z) >= height || p.X * p.X + p.Y * p.Y >= radius * radius) return false;
                }
                return true;
            }
            for (var i = 0; i < 3; i++)
            {
                var a = Axes[i];
                var reach = Math.Abs(delta.Dot(a)) + Profile.CellSize / 2 * (Math.Abs(a.X) + Math.Abs(a.Y) + Math.Abs(a.Z));
                var half = i == 0 ? Half.X : i == 1 ? Half.Y : Half.Z;
                if (reach >= half - Profile.BoundaryTolerance) return false;
            }
            return true;
        }
    }

    internal readonly record struct Projection(D3 Axis, double Radius);
    internal readonly record struct D3(double X, double Y, double Z)
    {
        public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public D3 Cross(D3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public D3 Abs() => new(Math.Abs(X), Math.Abs(Y), Math.Abs(Z));
        public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    }
}
