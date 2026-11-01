using System.Diagnostics;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionCylinderCoverageTests
{
    internal static void Run()
    {
        VerifyGeometry(); VerifyAxisAlignedReference(); VerifyComposition(); VerifyPrismReference(); VerifyLimits();
        var cylinder = Cylinder(new(240, 240, 160), 95, 79) with { Rotation = Quaternion.CreateFromYawPitchRoll(.2f, .1f, .3f) };
        var timer = Stopwatch.StartNew(); var measured = Preview([], [cylinder, cylinder]);
        Check(measured.Cells.Count is > 30_000 and < 100_000 && measured.CandidateTests < 2_000_000,
            "The representative rotated-cylinder workload stays bounded.");
        Console.WriteLine($"PASS circular cylinder coverage, mixed composition, independent prism reference and limits; {measured.Cells.Count:N0} cells / {measured.CandidateTests:N0} tests in {timer.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static void VerifyGeometry()
    {
        var one = Preview([], [Cylinder(new(10, 10, 10), 1, 1)]);
        Check(one.Cells.SequenceEqual(new[] { new OcclusionCoverageCoordinate(2, 2, 2) }), "A small circular cylinder covers one cell.");
        Check(one.CoordinateFingerprint == "c969d6c54092b8bc9199527b541f1a21c2221976312ef23a038f5ccf682563e2", "The shape-v2 coordinate fingerprint has a stable golden value.");
        var tangent = Cylinder(new(10, 10, 10), 2, 4);
        var contacts = Preview([], [tangent]);
        Check(contacts.Cells.Count == 15 && !contacts.Cells.Contains(new(1, 1, 2)),
            "Cap and side tangencies retain cells, while circular sides reject enclosing-box corners.");
        Check(Preview([], [Cylinder(new(10, 10, 10), MathF.Sqrt(8), 1)]).Cells.Count == 9,
            "Circular rim contact retains corner-touching cells.");
        Check(Preview([], [Cylinder(new(10, 10, 10), MathF.Sqrt(8) - .001f, 1)]).Cells.Count == 5,
            "A clear radial gap rejects corner cells.");
        Check(Preview([], [tangent with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) }]).Cells.Count == 15,
            "Rotation moves the cylinder axis without creating an ellipse.");
        Check(Preview([], [Cylinder(new(10, 10, 10), .000001f, .000001f)]).Cells.SequenceEqual(one.Cells),
            "A thin cylinder wholly inside the cube is found even when no cube corner lies inside its height slab.");
        var tilted = Cylinder(new(12, 10, 10), .02f, 8) with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4) };
        Check(Preview([], [tilted]).Cells.Contains(new(2, 2, 2)), "A thin tilted cylinder can cross a cube without containing cube vertices.");
        Check(Preview(one.Cells, [tangent with { Exclude = true, Radius = 3 }]).Cells.Count == 1,
            "Cap-tangent exclusions retain coverage.");
        Check(Preview(one.Cells, [tangent with { Exclude = true, Radius = MathF.Sqrt(8), Height = 5 }]).Cells.Count == 1,
            "Radially tangent or uncertain exclusions retain coverage.");
        Check(Preview(one.Cells, [tangent with { Exclude = true, Radius = 3, Height = 4.001f }]).Cells.Count == 0,
            "Cylinder exclusions remove clearly contained cell cubes.");
        Check(Preview(one.Cells, [Cylinder(new(10, 10, 10), 1, 5, true)]).Cells.Count == 1,
            "A cylinder containing only part of the cube cannot exclude it.");
        Check(Preview([], [Cylinder(new(524_278, 10, 10), .5f, 1)]).Cells.Single().X == 131_069,
            "Large structural coordinates retain stable radial calculations.");
        Check(Preview([], [Cylinder(new(10, 10, 10), float.Epsilon, float.Epsilon)]).Cells.SequenceEqual(one.Cells),
            "Positive subnormal dimensions are interpreted without squaring them to zero.");
    }

    private static void VerifyAxisAlignedReference()
    {
        var random = new Random(84261);
        for (var n = 0; n < 30; n++)
        {
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            var shape = Cylinder(new(Next(16, 24), Next(16, 24), Next(16, 24)), Next(.01f, 7), Next(.01f, 14));
            var actual = Preview([], [shape]).Cells.ToHashSet();
            var tolerance = OcclusionCoverageBuilder.GetPreviewProfile(GameId.UYA).BoundaryTolerance;
            for (var z = 1; z < 10; z++) for (var y = 1; y < 10; y++) for (var x = 1; x < 10; x++)
            {
                double Gap(double center, int cell) => Math.Max(0, Math.Max(cell * 4 - center, center - (cell * 4 + 4)));
                var dx = Gap(shape.Center.X, x); var dy = Gap(shape.Center.Y, y);
                var expected = Gap(shape.Center.Z, z) <= shape.Height / 2.0 + tolerance
                    && dx * dx + dy * dy <= (shape.Radius + tolerance) * (shape.Radius + tolerance);
                Check(actual.Contains(new(x, y, z)) == expected, "Axis-aligned cylinder must match analytic circle/rectangle and height-interval overlap.");
            }
        }
    }

    private static void VerifyComposition()
    {
        var box = new OcclusionCoverageBox(new(10, 10, 10), Quaternion.Identity, new(5, 5, 5));
        var exclude = Cylinder(new(10, 10, 10), 3, 5, true);
        var include = Cylinder(new(82, 82, 82), 1, 1);
        OcclusionCoverageCoordinate[] primary = [new(2, 2, 2), new(20, 20, 20)];
        var a = Preview(primary, [box, exclude, include, exclude]);
        var b = Preview(primary.Reverse().ToArray(), [exclude, include, exclude, box]);
        Check(a.Cells.Count == 27 && !a.Cells.Contains(new(2, 2, 2)) && a.Cells.Contains(new(20, 20, 20)),
            "Cylinder exclusions apply last to Primary and boxes while preserving a disconnected inclusion.");
        Check(a.SourceCellCounts.SequenceEqual(new[] { 27, 1, 1, 1 }), "Mixed-source counts preserve input order and independent exclusion contributions.");
        Check(a.Cells.SequenceEqual(b.Cells) && a.CoordinateFingerprint == b.CoordinateFingerprint,
            "Mixed source order cannot change coordinates or their fingerprint.");
        var legacy = OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA, primary, [box]);
        var shared = Preview(primary, [box]);
        Check(legacy.Cells.SequenceEqual(shared.Cells) && legacy.SourceCellCounts.SequenceEqual(shared.SourceCellCounts)
            && legacy.CandidateTests == shared.CandidateTests && legacy.CoordinateFingerprint != shared.CoordinateFingerprint,
            "The box-only wrapper keeps its geometry, counts and version-one identity while shared evaluation uses version two.");
        Check(OcclusionCoverageBuilder.GetPreviewProfile(GameId.UYA).AlgorithmVersion == 2
            && OcclusionCoverageBuilder.GetBoxPreviewProfile(GameId.UYA).AlgorithmVersion == 1, "Both algorithm identities remain explicit.");
        Check(Preview([], []).Cells.Count == 0, "Empty shape preview remains valid without native publication.");
        var opposite = include with { Rotation = new(0, 0, 0, -1) };
        Check(Preview([], [include]).CoordinateFingerprint == Preview([], [opposite]).CoordinateFingerprint,
            "Quaternion sign preserves circular coverage.");
        Check(primary[0] == new OcclusionCoverageCoordinate(2, 2, 2) && box.Size.X == 5,
            "Mixed evaluation preserves caller data.");
    }

    private static void VerifyPrismReference()
    {
        var random = new Random(537_811); var checkedInclusions = 0; var checkedExclusions = 0; var ambiguous = 0;
        OcclusionCoverageCoordinate[] primary = (from z in Enumerable.Range(1, 9) from y in Enumerable.Range(1, 9)
            from x in Enumerable.Range(1, 9) select new OcclusionCoverageCoordinate(x, y, z)).ToArray();
        for (var n = 0; n < 40; n++)
        {
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            var shape = Cylinder(new(Next(17, 24), Next(17, 24), Next(17, 24)), Next(.2f, 7), Next(.2f, 14))
                with { Rotation = Quaternion.CreateFromYawPitchRoll(Next(-3, 3), Next(-3, 3), Next(-3, 3)) };
            var actual = Preview([], [shape]).Cells.ToHashSet();
            var remaining = Preview(primary, [shape with { Exclude = true }]).Cells.ToHashSet();
            var reference = PrismAxes(shape);
            foreach (var cell in primary)
            {
                var center = new Vector3(cell.X * 4 + 2, cell.Y * 4 + 2, cell.Z * 4 + 2);
                var delta = center - shape.Center;
                var inner = reference.All(a => Math.Abs(Vector3.Dot(a.Direction, delta)) <= a.InnerRadius - .0001);
                var outer = reference.All(a => Math.Abs(Vector3.Dot(a.Direction, delta)) <= a.OuterRadius + .0001);
                if (inner) { checkedInclusions++; Check(actual.Contains(cell), "Cylinder must retain every cell intersecting a strictly inscribed prism."); }
                else if (!outer) { checkedInclusions++; Check(!actual.Contains(cell), "Cylinder must reject every cell clearly separated from its circumscribed prism."); }
                else ambiguous++;
                var contained = true; var outside = false;
                for (var corner = 0; corner < 8; corner++)
                {
                    var p = center + new Vector3((corner & 1) == 0 ? -2 : 2, (corner & 2) == 0 ? -2 : 2, (corner & 4) == 0 ? -2 : 2);
                    var local = Vector3.Transform(p - shape.Center, Quaternion.Conjugate(shape.Rotation));
                    var radial = Math.Sqrt((double)local.X * local.X + (double)local.Y * local.Y);
                    contained &= radial < shape.Radius - .0001 && Math.Abs(local.Z) < shape.Height / 2 - .0001;
                    outside |= radial > shape.Radius + .0001 || Math.Abs(local.Z) > shape.Height / 2 + .0001;
                }
                if (contained) { checkedExclusions++; Check(!remaining.Contains(cell), "A rotated cylinder excludes cubes with every corner clearly contained."); }
                if (outside) { checkedExclusions++; Check(remaining.Contains(cell), "A rotated cylinder retains cubes with an outside corner."); }
            }
            var spun = shape with { Rotation = Quaternion.Normalize(shape.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .713f)) };
            Check(Preview([], [spun]).Cells.SequenceEqual(actual.OrderBy(c => c.Z).ThenBy(c => c.Y).ThenBy(c => c.X)),
                "Spinning around the cylinder's own axis cannot create an elliptical cross-section.");
        }
        Check(checkedInclusions > 28_000 && checkedExclusions > 28_000 && ambiguous < 100,
            "Independent prism and containment references must exercise broad randomized coverage with bounded ambiguity.");
    }

    private static Axis[] PrismAxes(OcclusionCoverageCylinder cylinder)
    {
        const int sides = 64;
        var x = Vector3.Transform(Vector3.UnitX, cylinder.Rotation); var y = Vector3.Transform(Vector3.UnitY, cylinder.Rotation);
        var z = Vector3.Transform(Vector3.UnitZ, cylinder.Rotation);
        var ring = Enumerable.Range(0, sides).Select(i => x * MathF.Cos(2 * MathF.PI * i / sides) + y * MathF.Sin(2 * MathF.PI * i / sides)).ToArray();
        var axes = new List<Vector3> { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, z };
        Vector3[] world = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        foreach (var direction in world) axes.Add(Vector3.Cross(direction, z));
        for (var i = 0; i < sides; i++)
        {
            var edge = ring[(i + 1) % sides] - ring[i];
            axes.Add(Vector3.Cross(edge, z)); foreach (var direction in world) axes.Add(Vector3.Cross(direction, edge));
        }
        return axes.Where(a => a.LengthSquared() > 1e-12).Select(a =>
        {
            var unit = Vector3.Normalize(a); var support = ring.Max(p => Math.Abs(Vector3.Dot(p, unit))) * cylinder.Radius;
            var height = Math.Abs(Vector3.Dot(z, unit)) * cylinder.Height / 2;
            var cube = 2 * (Math.Abs(unit.X) + Math.Abs(unit.Y) + Math.Abs(unit.Z));
            return new Axis(unit, support + height + cube, support / Math.Cos(Math.PI / sides) + height + cube);
        }).ToArray();
    }

    private static void VerifyLimits()
    {
        var valid = Cylinder(new(20, 20, 20), 2, 3);
        Throws<ArgumentNullException>(() => Preview(null!, [])); Throws<ArgumentNullException>(() => Preview([], null!));
        foreach (var bad in new[] { valid with { Radius = 0 }, valid with { Radius = -1 }, valid with { Radius = float.NaN },
            valid with { Radius = float.PositiveInfinity }, valid with { Radius = 1_000_001 }, valid with { Height = 0 },
            valid with { Height = -1 }, valid with { Height = float.NaN }, valid with { Height = float.PositiveInfinity },
            valid with { Height = 1_000_001 }, valid with { Center = new(float.NaN, 10, 10) },
            valid with { Rotation = default }, valid with { Rotation = new(0, 0, 0, 2) },
            valid with { Rotation = new(float.NaN, 0, 0, 1) }, valid with { Center = new(0, 10, 10) },
            valid with { Center = new(524_282, 10, 10) } })
            Throws<InvalidDataException>(() => Preview([], [bad]));
        Throws<InvalidDataException>(() => Preview([], [null!]));
        Throws<InvalidDataException>(() => Preview([], [new UnsupportedShape()]));
        Throws<InvalidDataException>(() => Preview([], Enumerable.Repeat<OcclusionCoverageShape>(valid, 1025).ToArray()));
        Check(Throws<InvalidDataException>(() => Preview([], [Cylinder(new(2000, 2000, 2000), 1000, 1000)])).Message.Contains("candidate tests"),
            "Cylinder candidate bounds fail before enumeration when work is excessive.");
        Check(Throws<InvalidDataException>(() => Preview([], [Cylinder(new(300, 300, 150), 150, 250)])).Message.Contains("inclusion union"),
            "Cylinder results cannot exceed the same pre-exclusion memory budget as boxes.");
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.PreviewShapes(GameId.UYA, [], [valid], new(true)));
        using var cancellation = new CancellationTokenSource();
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.PreviewShapes(GameId.UYA, [], new CancellingShapes(cancellation), cancellation.Token));
        Throws<NotSupportedException>(() => OcclusionCoverageBuilder.PreviewShapes(GameId.DL, [], []));
        Throws<NotSupportedException>(() => OcclusionCoverageBuilder.GetPreviewProfile(GameId.GC));
    }

    private sealed record UnsupportedShape() : OcclusionCoverageShape(new(10, 10, 10), Quaternion.Identity, false);
    private sealed class CancellingShapes(CancellationTokenSource cancellation) : IReadOnlyList<OcclusionCoverageShape>
    {
        public int Count => 1;
        public OcclusionCoverageShape this[int index] { get { cancellation.Cancel(); return Cylinder(new(40, 40, 40), 19, 39); } }
        public IEnumerator<OcclusionCoverageShape> GetEnumerator() { yield return this[0]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private readonly record struct Axis(Vector3 Direction, double InnerRadius, double OuterRadius);
    private static OcclusionCoverageCylinder Cylinder(Vector3 center, float radius, float height, bool exclude = false) => new(center, Quaternion.Identity, radius, height, exclude);
    private static OcclusionCoveragePreview Preview(IReadOnlyList<OcclusionCoverageCoordinate> primary, IReadOnlyList<OcclusionCoverageShape> shapes)
        => OcclusionCoverageBuilder.PreviewShapes(GameId.UYA, primary, shapes);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; } throw new Exception($"Expected {typeof(T).Name}.");
    }
}
