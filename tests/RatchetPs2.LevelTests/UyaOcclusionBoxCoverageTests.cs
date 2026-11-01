using System.Diagnostics;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionBoxCoverageTests
{
    internal static void Run()
    {
        VerifyGeometry(); VerifyComposition(); VerifyReference(); VerifyLimits();
        var timer = Stopwatch.StartNew();
        var workload = Box(new(400, 400, 40), new(399, 399, 39));
        var measured = Preview([], [workload, workload]);
        Check(measured.Cells.Count == 100_000 && measured.CandidateTests == 200_000,
            "The representative 100,000-cell preview must report bounded candidate work.");
        Console.WriteLine($"PASS box coverage, independent edge/face reference, deterministic composition and limits; 100,000 cells in {timer.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static void VerifyGeometry()
    {
        var one = Preview([], [Box(new(10, 10, 10), new(1, 1, 1))]);
        Check(one.Cells.SequenceEqual(new[] { new OcclusionCoverageCoordinate(2, 2, 2) }), "A small centered box covers one cell.");
        Check(one.CoordinateFingerprint == "4d56d5c77565f744140ae35cb8c8e962c49e88ef96c34a23699c68dee768b956", "Algorithm v1 has a stable coordinate fingerprint.");
        var aligned = Preview([], [Box(new(10, 10, 10), new(4, 4, 4))]);
        Check(aligned.Cells.Count == 27, "Face, edge and corner tangencies retain adjacent cells.");
        var thin = Preview([], [Box(new(8, 10, 10), new(0.000001f, 1, 1))]);
        Check(thin.Cells.SequenceEqual(new[] { new OcclusionCoverageCoordinate(1, 2, 2), new(2, 2, 2) }),
            "A thin box on a cell plane retains both intersecting cells.");
        var diamond = Box(new(10, 10, 10), new(2 * MathF.Sqrt(2), 2 * MathF.Sqrt(2), 1))
            with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4) };
        Check(Preview([], [diamond]).Cells.SequenceEqual(new[] { new OcclusionCoverageCoordinate(2, 1, 2),
            new(1, 2, 2), new(2, 2, 2), new(3, 2, 2), new(2, 3, 2) }),
            "Rotated boxes must reject AABB corners while retaining tangent vertices.");
        var tangent = Box(new(10, 10, 10), new(4, 4, 4), true);
        Check(Preview(one.Cells, [tangent]).Cells.Count == 1, "Exclusions retain cells touching the box boundary.");
        Check(Preview(one.Cells, [tangent with { Size = new(4.001f, 4.001f, 4.001f) }]).Cells.Count == 0,
            "Exclusions remove a cell only when its entire cube is clearly inside.");
        Check(Preview(one.Cells, [tangent with { Size = new(4.000001f, 4.000001f, 4.000001f) }]).Cells.Count == 1,
            "Uncertain containment within the tolerance retains coverage.");
        Check(Preview([], [Box(new(524_278, 10, 10), new(1, 1, 1))]).Cells.Single().X == 131_069,
            "The structural maximum coordinate must remain an integer without wrapping.");
    }

    private static void VerifyComposition()
    {
        OcclusionCoverageCoordinate[] primary = [new(2, 2, 2), new(50, 40, 30), new(2, 2, 2)];
        var include = Box(new(10, 10, 10), new(5, 5, 5));
        var exclude = Box(new(10, 10, 10), new(4.001f, 4.001f, 4.001f), true);
        var a = Preview(primary, [include, exclude, include, exclude]);
        var b = Preview(primary.Reverse().ToArray(), [exclude, include, exclude, include]);
        Check(a.Cells.Count == 27 && !a.Cells.Contains(new(2, 2, 2)) && a.Cells.Contains(new(50, 40, 30)),
            "Exclusions apply last to both Primary and authored coverage; disconnected islands survive.");
        Check(a.Cells.SequenceEqual(b.Cells) && a.CoordinateFingerprint == b.CoordinateFingerprint,
            "Input order and duplicate contributions cannot change canonical coordinates or their fingerprint.");
        Check(a.SourceCellCounts.SequenceEqual(new[] { 27, 1, 27, 1 }), "Counts describe each source before exclusion removal.");
        Check(primary.Length == 3 && include.Size == new Vector3(5, 5, 5), "Preview cannot mutate caller inputs.");
        var empty = Preview([], []);
        Check(empty.Cells.Count == 0 && empty.CandidateTests == 0 && empty.CoordinateFingerprint.Length == 64,
            "Empty preview is valid without authorizing native empty publication.");
        Check(empty.CoordinateFingerprint != a.CoordinateFingerprint, "Different coordinates require different fingerprints.");
        Check(Preview(primary, []).Cells.Count == 2, "Primary-only preview deduplicates coordinates.");
    }

    private static void VerifyReference()
    {
        // Independent convex-box reference: corners inside either solid, or an edge crossing the other's faces.
        var random = new Random(781223);
        for (var n = 0; n < 80; n++)
        {
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            var box = Box(new(Next(12, 24), Next(12, 24), Next(12, 24)), new(Next(.1f, 12), Next(.1f, 12), Next(.1f, 12)))
                with { Rotation = Quaternion.CreateFromYawPitchRoll(Next(-3, 3), Next(-3, 3), Next(-3, 3)) };
            var actual = Preview([], [box]).Cells.ToHashSet();
            var primary = (from z in Enumerable.Range(0, 10) from y in Enumerable.Range(0, 10) from x in Enumerable.Range(0, 10)
                select new OcclusionCoverageCoordinate(x, y, z)).ToArray();
            var remaining = Preview(primary, [box with { Exclude = true }]).Cells.ToHashSet();
            for (var z = 0; z < 10; z++) for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++)
            {
                var cell = new OcclusionCoverageCoordinate(x, y, z);
                var center = new Vector3(x * 4 + 2, y * 4 + 2, z * 4 + 2);
                var unit = Quaternion.Identity;
                var shape = Corners(box.Center, box.Rotation, box.Size / 2);
                var cube = Corners(center, unit, new(2));
                var expected = shape.Any(p => Inside(p, center, unit, new(2)))
                    || cube.Any(p => Inside(p, box.Center, box.Rotation, box.Size / 2))
                    || EdgesHit(shape, center, unit, new(2))
                    || EdgesHit(cube, box.Center, box.Rotation, box.Size / 2);
                var contained = cube.All(p => Inside(p, box.Center, box.Rotation, box.Size / 2 - new Vector3(.00001f)));
                Check(remaining.Contains(cell) == !contained, "Rotated exclusion must agree with independent corner containment.");
                Check(actual.Contains(cell) == expected, $"Rotated box {n}, cell {cell} differs from the independent face/edge reference.");
            }
            var negated = box with { Rotation = new(-box.Rotation.X, -box.Rotation.Y, -box.Rotation.Z, -box.Rotation.W) };
            Check(Preview([], [negated]).CoordinateFingerprint == Preview([], [box]).CoordinateFingerprint,
                "Equivalent quaternion signs cannot change coverage.");
        }
    }

    private static Vector3[] Corners(Vector3 center, Quaternion rotation, Vector3 half) => Enumerable.Range(0, 8)
        .Select(i => center + Vector3.Transform(new Vector3((i & 1) == 0 ? -half.X : half.X,
            (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z), rotation)).ToArray();

    private static bool Inside(Vector3 point, Vector3 center, Quaternion rotation, Vector3 half)
    {
        var p = Vector3.Abs(Vector3.Transform(point - center, Quaternion.Conjugate(rotation)));
        return p.X <= half.X && p.Y <= half.Y && p.Z <= half.Z;
    }

    private static bool EdgesHit(Vector3[] corners, Vector3 center, Quaternion rotation, Vector3 half)
    {
        for (var i = 0; i < 8; i++) for (var axis = 0; axis < 3; axis++)
        {
            var other = i ^ (1 << axis); if (i > other) continue;
            var a = Vector3.Transform(corners[i] - center, Quaternion.Conjugate(rotation));
            var b = Vector3.Transform(corners[other] - center, Quaternion.Conjugate(rotation));
            var delta = b - a; var low = 0f; var high = 1f;
            for (var d = 0; d < 3; d++)
            {
                var p = d == 0 ? a.X : d == 1 ? a.Y : a.Z;
                var v = d == 0 ? delta.X : d == 1 ? delta.Y : delta.Z;
                var h = d == 0 ? half.X : d == 1 ? half.Y : half.Z;
                if (MathF.Abs(v) < 1e-10f) { if (MathF.Abs(p) > h) { high = -1; break; } }
                else { var t1 = (-h - p) / v; var t2 = (h - p) / v;
                    low = MathF.Max(low, MathF.Min(t1, t2)); high = MathF.Min(high, MathF.Max(t1, t2)); }
            }
            if (low <= high) return true;
        }
        return false;
    }

    private static void VerifyLimits()
    {
        var box = Box(new(10, 10, 10), new(1, 1, 1));
        Throws<ArgumentNullException>(() => Preview(null!, []));
        Throws<ArgumentNullException>(() => Preview([], null!));
        foreach (var bad in new[] { box with { Center = new(float.NaN, 10, 10) }, box with { Center = new(float.PositiveInfinity, 10, 10) },
            box with { Size = new(0, 1, 1) }, box with { Size = new(-1, 1, 1) }, box with { Size = new(float.NaN, 1, 1) },
            box with { Size = new(1_000_001, 1, 1) }, box with { Rotation = default },
            box with { Rotation = new(float.NaN, 0, 0, 1) }, box with { Rotation = new(0, 0, 0, 2) },
            box with { Center = new(0, 10, 10) }, box with { Center = new(524_282, 10, 10) },
            box with { Center = new(2, 10, 10), Size = new(4, 1, 1) } })
            Throws<InvalidDataException>(() => Preview([], [bad]));
        Throws<InvalidDataException>(() => Preview([], [null!]));
        Throws<InvalidDataException>(() => Preview([new(-1, 2, 2)], []));
        Throws<InvalidDataException>(() => Preview([new(131_070, 2, 2)], []));
        Throws<InvalidDataException>(() => Preview([], Enumerable.Repeat(box, 1025).ToArray()));
        Throws<InvalidDataException>(() => Preview(Enumerable.Repeat(new OcclusionCoverageCoordinate(2, 2, 2), 100_001).ToArray(), []));
        var workError = Throws<InvalidDataException>(() => Preview([], [Box(new(2000, 2000, 2000), new(1000, 1000, 1000))]));
        Check(workError.Message.Contains("candidate tests"), "Large candidate spans fail during preflight.");
        var tooMany = Box(new(404, 404, 44), new(400, 400, 80));
        Check(Throws<InvalidDataException>(() => Preview([], [tooMany])).Message.Contains("inclusion union"),
            "The memory bound applies to the inclusion union before exclusions.");
        var exclusions = Enumerable.Repeat(box with { Exclude = true }, 30).Prepend(Box(new(400, 400, 40), new(399, 399, 39))).ToArray();
        Check(Throws<InvalidDataException>(() => Preview([], exclusions)).Message.Contains("candidate tests"),
            "Preflight includes worst-case exclusion work before enumerating inclusion cells.");
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA, [], [box], new(true)));
        using var cancellation = new CancellationTokenSource();
        var cancelling = new CancellingPrimary(cancellation);
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA, cancelling, [box], cancellation.Token));
        using var duringEnumeration = new CancellationTokenSource();
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA, [],
            new CancellingBoxes(duringEnumeration), duringEnumeration.Token));
        Throws<NotSupportedException>(() => OcclusionCoverageBuilder.PreviewBoxes(GameId.DL, [], []));
        Throws<NotSupportedException>(() => OcclusionCoverageBuilder.GetBoxPreviewProfile(GameId.RC1));
        Check(OcclusionCoverageBuilder.GetBoxPreviewProfile(GameId.UYA).CellSize == 4, "The target SDK supplies its cell dimensions.");
    }

    private sealed class CancellingBoxes(CancellationTokenSource cancellation) : IReadOnlyList<OcclusionCoverageBox>
    {
        public int Count => 1;
        public OcclusionCoverageBox this[int index] { get { cancellation.Cancel(); return Box(new(40, 40, 40), new(39, 39, 39)); } }
        public IEnumerator<OcclusionCoverageBox> GetEnumerator() { yield return this[0]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancellingPrimary(CancellationTokenSource cancellation) : IReadOnlyList<OcclusionCoverageCoordinate>
    {
        public int Count => 1;
        public OcclusionCoverageCoordinate this[int index] => new(2, 2, 2);
        public IEnumerator<OcclusionCoverageCoordinate> GetEnumerator() { cancellation.Cancel(); yield return new(2, 2, 2); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static OcclusionCoverageBox Box(Vector3 center, Vector3 size, bool exclude = false) => new(center, Quaternion.Identity, size, exclude);
    private static OcclusionCoveragePreview Preview(IReadOnlyList<OcclusionCoverageCoordinate> primary, IReadOnlyList<OcclusionCoverageBox> boxes)
        => OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA, primary, boxes);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; } throw new Exception($"Expected {typeof(T).Name}.");
    }
}
