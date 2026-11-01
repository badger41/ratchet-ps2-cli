using System.Diagnostics;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionGraphCoverageTests
{
    private static readonly OcclusionCoverageGraph Room = new(new(16, 16, 16), Quaternion.Identity, new(23), [new(-6, -3.7f, -4.3f)]);
    private static readonly OcclusionSurfaceTriangle[] Wall = Quad(new(16, 0, 0), new(16, 32, 0), new(16, 32, 32), new(16, 0, 32));

    internal static void Run()
    {
        VerifyRooms(); VerifyCylinderBounds(); VerifyComposition(); VerifyQueries(); VerifyLimits(); VerifyAcceleration(); UyaOcclusionStickyCoverageTests.Run();
        Console.WriteLine("PASS graph coverage: rooms/doorway, composition, probes, alpha/backfaces, buried seeds, ownership, bounds and cancellation.");
    }

    private static void VerifyRooms()
    {
        var open = Preview([Room]);
        Check(open.Graphs[0].Accepted.Count == 216 && open.Graphs[0].Rejected.Count == 0, "An empty scene retains every candidate.");
        var closed = Preview([Room], Wall);
        Check(closed.Graphs[0].Accepted.Count == 108 && closed.Graphs[0].Rejected.Count == 108,
            "A wall splits the candidate region into reachable and unreachable halves.");
        Check(closed.Coverage.Cells.All(c => c.X < 4), "The independent axis-plane reference retains exactly the seed side.");
        var doorway = Quad(new(16, 0, 0), new(16, 10, 0), new(16, 10, 32), new(16, 0, 32))
            .Concat(Quad(new(16, 14, 0), new(16, 32, 0), new(16, 32, 32), new(16, 14, 32))).ToArray();
        Check(Preview([Room], doorway).Graphs[0].Accepted.Count > 108, "A doorway restores visible cells on the other side.");
        var bothRooms = Preview([Room with { Seeds = [Room.Seeds[0], new(6, -3.7f, -4.3f)] }], Wall);
        Check(bothRooms.Coverage.Cells.Count == 216,
            "Seeds on both sides restore both rooms without graph edges.");
        Check(bothRooms.Rays < 300, "Nearby seeds avoid probing through the other room's wall before finding a reachable seed.");
        Check(Preview([Room with { Seeds = [new(6, -3.7f, -4.3f), Room.Seeds[0]] }], Wall).Coverage.Cells.Count == 216,
            "Seed order cannot change coverage.");
        var shuffled = Preview([Room], Wall.Reverse().Select(t => t with { A = t.C, C = t.A }).ToArray());
        Check(shuffled.Coverage.CoordinateFingerprint == closed.Coverage.CoordinateFingerprint, "Triangle order and backface winding do not change coverage.");
        Check(Preview([Room with { Rotation = new(0, 0, 0, -1) }], Wall).Coverage.CoordinateFingerprint == closed.Coverage.CoordinateFingerprint,
            "Quaternion sign cannot change graph coordinates.");
        var offset = new Vector3(400_000);
        var high = Preview([Room with { Center = Room.Center + offset }], Wall.Select(t => t with { A = t.A + offset, B = t.B + offset, C = t.C + offset }).ToArray());
        Check(high.Coverage.Cells.Count == 108 && high.Coverage.Cells.All(c => c.X < 100_004), "Large coordinates retain double-precision probe/seed transforms.");
        var rotated = Room with { Center = new(40, 40, 40), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) };
        Check(Preview([rotated]).Coverage.Cells.Count == 216, "Rotated graphs transform local seeds and regions together.");
        Check(closed.Coverage.CoordinateFingerprint == "9a1c04340679b1298d29fe30d0e0c5689f114fb0ab3ea246b7cd8220ccdf56f4", "Graph coordinates retain their version-three golden identity.");
    }

    private static void VerifyCylinderBounds()
    {
        var graph = new OcclusionCoverageGraph(new(64), Quaternion.Identity, new(64), [Vector3.Zero]) { CylinderRadius = 32 };
        foreach (var rotation in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(.2f, .4f, .7f) })
        {
            var current = graph with { Rotation = rotation };
            var cylinder = new OcclusionCoverageCylinder(current.Center, rotation, 32, 64);
            var expected = OcclusionCoverageBuilder.PreviewShapes(GameId.UYA, [], [cylinder]);
            var actual = Preview([current]);
            Check(actual.Coverage.Cells.SequenceEqual(expected.Cells), "Cylinder graphs reuse exact cylinder/cell intersections, including rotated boundaries.");
            var box = Preview([current with { CylinderRadius = null }]);
            Check(actual.Coverage.Cells.Count < box.Coverage.Cells.Count && actual.Coverage.Cells.All(box.Coverage.Cells.Contains),
                "Cylinder graph boundaries remove box-corner cells without losing intersecting boundary cells.");
        }
        Throws(() => Preview([graph with { Seeds = [new(30, 30, 0)] }]), "inside");
        Throws(() => Preview([graph with { Size = new(63, 64, 64) }]), "diameter");
        foreach (var radius in new[] { 0f, -1, float.NaN, float.PositiveInfinity })
            Throws(() => Preview([graph with { CylinderRadius = radius }]), "cylinder");
    }

    private static void VerifyComposition()
    {
        var graphOnly = Preview([Room], Wall);
        var rescue = new OcclusionCoverageBox(new(22, 10, 10), Quaternion.Identity, Vector3.One);
        var exclusion = new OcclusionCoverageBox(new(10, 10, 10), Quaternion.Identity, new(4.1f), true);
        var result = Preview([Room], Wall, [rescue, exclusion], [new(6, 6, 6)]);
        Check(result.Coverage.Cells.Contains(new(5, 2, 2)), "An include shape restores a graph-rejected cell.");
        Check(result.Coverage.Cells.Contains(new(6, 6, 6)), "Primary cells survive graph rejection.");
        Check(!result.Coverage.Cells.Contains(new(2, 2, 2)), "Final exclusions remove graph/Primary/shape union cells.");
        Check(result.Graphs[0].Accepted.SequenceEqual(graphOnly.Graphs[0].Accepted), "Graph inspection precedes exclusions and rescue shapes.");
        Check(result.Coverage.SourceCellCounts.SequenceEqual(new[] { 1, 1 }), "Ordinary shape counts retain their input order.");
        var reverse = Preview([Room], Wall, [exclusion, rescue], [new(6, 6, 6)]);
        Check(reverse.Coverage.CoordinateFingerprint == result.Coverage.CoordinateFingerprint, "Composition order cannot change coordinates.");
        var duplicate = Preview([Room, Room], Wall);
        Check(duplicate.Coverage.CoordinateFingerprint == graphOnly.Coverage.CoordinateFingerprint, "Overlapping graph coverage deduplicates.");
        var other = Room with { Seeds = [new(6, -3.7f, -4.3f)] };
        var owned = Preview([Room, other], Wall);
        Check(Preview([other, Room], Wall).Coverage.CoordinateFingerprint == owned.Coverage.CoordinateFingerprint,
            "Graph source order cannot change the effective set.");
        Check(owned.Graphs[0].Accepted.All(c => c.X < 4) && owned.Graphs[1].Accepted.All(c => c.X >= 4), "Each graph filters using only its own seeds.");
    }

    private static void VerifyQueries()
    {
        foreach (var flags in new uint[] { 0, 2 }) Check(Preview([Room], Wall.Select(t => t with { Flags = flags }).ToArray()).Coverage.Cells.Count == 216,
            "Target-only/nonblocking faces never filter coverage.");
        foreach (var alpha in new byte[] { 0, 64, 126, 127 })
            Check(Preview([Room], Wall.Select(t => t with { TextureIndex = 0 }).ToArray(), textures: [new(1, 1, [alpha])]).Coverage.Cells.Count == 216,
                "Transparent and threshold-uncertain hits retain coverage.");
        Check(Preview([Room], Wall.Select(t => t with { TextureIndex = 0 }).ToArray(), textures: [new(1, 1, [128])]).Coverage.Cells.Count == 108,
            "Raw PS2 alpha above threshold blocks using existing bilinear sampling.");
        var cube = Cube(new(8), new(12));
        var buried = Room with { Seeds = [new(-6, -6, -6)] };
        try { Preview([buried], cube); throw new Exception("Buried seed was accepted."); }
        catch (OcclusionCoverageSeedException e) { Check(e.GraphIndex == 0 && e.SeedIndex == 0, "Buried-seed diagnostics expose stable input indices."); }
        var doubleSided = cube.Concat(cube.Select(t => t with { A = t.C, C = t.A })).ToArray();
        Check(Preview([buried], doubleSided).Coverage.CoordinateFingerprint == Preview([buried], doubleSided.Reverse().ToArray()).Coverage.CoordinateFingerprint,
            "Opposite-winding coincident exits remain ambiguous independently of input order.");
        var graph = new OcclusionCoverageGraph(new(10), Quaternion.Identity, Vector3.One, [new(0, 0, 0)]);
        var query = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery(Wall, [], default);
        var a = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry.D3(10, 12, 10);
        Check(query.Blocked(a, new(20, 12, 10)), "Finite interior intersections block.");
        Check(!query.Blocked(a, new(15, 12, 10)), "Geometry beyond the seed does not block a finite segment.");
        Check(!query.Blocked(a, new(16, 12, 10)), "Endpoint hits retain coverage.");
        Check(!query.Blocked(new(16, 12, 10), new(20, 12, 10)), "Origin hits retain coverage.");
        Check(!query.Blocked(new(10, 10, 10), new(20, 10, 10)), "Shared triangle-edge hits remain uncertain.");
        Check(!query.Blocked(a, a), "Coincident probe and seed retain coverage.");
        var random = new Random(6812);
        var planes = new[] { 10f, 20f, 30f };
        var walls = planes.SelectMany(x => Quad(new(x, 0, 0), new(x, 32, 0), new(x, 32, 32), new(x, 0, 32))).ToArray();
        var accelerated = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery(walls, [], default);
        for (var i = 0; i < 200; i++)
        {
            RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageGeometry.D3 Point() => new(2 + random.NextDouble() * 36, 2 + random.NextDouble() * 28, 2 + random.NextDouble() * 28);
            var from = Point(); var to = Point();
            var expected = planes.Any(x => x > Math.Min(from.X, to.X) + .001 && x < Math.Max(from.X, to.X) - .001);
            Check(accelerated.Blocked(from, to) == expected, "Accelerated segment results agree with independent axis-plane intersections.");
        }
        var noBlock = Preview([graph], []); Check(noBlock.Coverage.Cells.Count == 1, "Tiny regions retain crossing cells.");
    }

    private static void VerifyLimits()
    {
        Throws(() => Preview([Room with { Seeds = [] }]), "seed");
        Throws(() => Preview([Room with { Seeds = null! }]), "seed");
        Throws(() => Preview(Enumerable.Repeat(Room, 1025).ToArray()), "source");
        var tiny = new OcclusionCoverageGraph(new(10), Quaternion.Identity, Vector3.One, Enumerable.Repeat(Vector3.Zero, 4096).ToArray());
        Throws(() => Preview(Enumerable.Repeat(tiny, 5).ToArray()), "combined seed");
        Throws(() => Preview([Room], primary: Enumerable.Range(0, 100_000).Select(x => new OcclusionCoverageCoordinate(x, 1, 1)).ToArray()), "union");
        Throws(() => Preview([Room with { Seeds = [new(float.NaN, 0, 0)] }]), "seed");
        Throws(() => Preview([Room with { Seeds = [new(12, 0, 0)] }]), "inside");
        Throws(() => Preview([Room with { Rotation = new(0, 0, 0, 0) }]), "rotation");
        Throws(() => Preview([Room with { Center = Vector3.Zero }]), "coordinate");
        Throws(() => Preview([Room with { Center = new(1000), Size = new(1000) }]), "candidate");
        Throws(() => Preview([Room with { Seeds = Enumerable.Repeat(Vector3.Zero, 4097).ToArray() }]), "seed");
        var manySeeds = Preview([Room with { Seeds = Enumerable.Repeat(Vector3.Zero, 4096).ToArray() }]);
        Check(manySeeds.Coverage.Cells.Count == 216 && manySeeds.Rays < 5_000,
            "Open cells stop after a reachable seed rather than reserving nine probes for every seed.");
        var mapRegion = new OcclusionCoverageGraph(new(255.09949f, 255.72794f, 77.00286f), Quaternion.Identity,
            new(306.55453f, 292.03552f, 32), Enumerable.Repeat(Vector3.Zero, 16).ToArray());
        var mapPreview = Preview([mapRegion]);
        Check(mapPreview.Coverage.Cells.Count == 51_948 && mapPreview.Rays < 60_000,
            "A 52k-cell/sixteen-seed region completes within actual limits despite exceeding the old worst-case estimate.");
        var rayBudget = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery([], [], default);
        Throws(() =>
        {
            for (var i = 0; i <= OcclusionCoverageBuilder.GetGraphPreviewProfile(GameId.UYA).MaximumRays; i++)
                rayBudget.Blocked(new(1, 1, 1), new(2, 2, 2));
        }, "ray limit");
        Check(rayBudget.Rays == OcclusionCoverageBuilder.GetGraphPreviewProfile(GameId.UYA).MaximumRays + 1,
            "Every attempted ray remains bounded at runtime, including empty-scene queries.");
        Throws(() => Preview([Room], Wall.Select(t => t with { Flags = 4 }).ToArray()), "flags");
        Throws(() => Preview([Room], Wall.Select(t => t with { TextureIndex = 0 }).ToArray()), "texture");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [Room], [], [], cancellation.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }
        try { OcclusionCoverageBuilder.PreviewGraphs(GameId.RC1, [], [], [], [], []); throw new Exception("Unsupported game accepted."); }
        catch (NotSupportedException) { }
        var progress = new List<(int Done, int Total)>();
        var large = Room with { Center = new(40), Size = new(32), Seeds = [Vector3.Zero] };
        OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [large], [], [], progress: (done, total) => progress.Add((done, total)));
        Check(progress.Count >= 4 && progress[0].Done == 0 && progress[^1].Done == progress[^1].Total
            && progress.Select(p => p.Done).SequenceEqual(progress.Select(p => p.Done).Order()), "Progress reaches bounded checkpoints monotonically.");
        using var during = new CancellationTokenSource();
        try
        {
            OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [large], [], [], during.Token,
                (done, total) => { if (done >= 256) during.Cancel(); });
            throw new Exception("Enumeration cancellation was ignored.");
        }
        catch (OperationCanceledException) { }
        var parallel = new OcclusionSurfaceTriangle(new(0, 0, 10), new(32, 0, 10), new(0, 32, 10), -1, 1);
        var work = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery(Enumerable.Repeat(parallel, 2048).ToArray(), [], default);
        try
        {
            for (var i = 0; i < 100_000; i++) work.Blocked(new(10, 12, 10), new(20, 12, 10));
            throw new Exception("The query-work budget was ignored.");
        }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("work limit (200,000,000 geometry tests after")
                && error.Message.Contains(FormattableString.Invariant($"{work.Rays:N0} rays; 2,048 scene triangles")),
                "Budget diagnostics report the actual limit, rays attempted and scene size.");
        }
        using var mid = new CancellationTokenSource(); mid.CancelAfter(1);
        try { OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [Room], Enumerable.Repeat(Wall[0], 200_000).ToArray(), [], mid.Token); throw new Exception("Running cancellation was ignored."); }
        catch (OperationCanceledException) { }
    }

    private static void VerifyAcceleration()
    {
        var triangles = new List<OcclusionSurfaceTriangle>(Wall);
        for (var i = 0; i < 10_000; i++)
        {
            var x = 1000 + i * 2;
            triangles.Add(new(new(x, 1000, 1000), new(x + 1, 1000, 1000), new(x, 1001, 1000), -1, 1));
        }
        var timer = Stopwatch.StartNew(); var result = Preview([Room], triangles);
        Check(result.Coverage.Cells.Count == 108 && result.QueryTests < 200_000, "The spatial index prunes distant blockers without changing results.");
        Console.WriteLine($"Graph workload: {result.Coverage.Cells.Count} accepted / {result.Graphs[0].Rejected.Count} rejected, {result.Rays} rays, {result.QueryTests} query tests in {timer.Elapsed.TotalMilliseconds:F0} ms");
        // Coincident opaque surfaces should not exhaust the budget after a definite blocker is found.
        var layered = Enumerable.Range(0, 20_000).SelectMany(_ => Wall).ToArray();
        timer.Restart();
        var dense = Preview([Room], layered);
        Check(dense.Coverage.CoordinateFingerprint == Preview([Room], Wall).Coverage.CoordinateFingerprint,
            "Repeated opaque surfaces preserve accepted/rejected coverage and its golden identity.");
        var blocked = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery(Enumerable.Repeat(Wall[0], 2048).ToArray(), [], default);
        Check(blocked.Blocked(new(10, 12, 10), new(20, 12, 10)) && blocked.Tests < 100,
            "A definite opaque hit does not test every remaining overlapping blocker.");
        var firstTests = blocked.Tests;
        Check(blocked.Blocked(new(10, 12, 10), new(20, 12, 10)) && blocked.Tests - firstTests == 1,
            "A repeated blocked segment rechecks its coherent face without traversing the index.");
        Check(!blocked.Blocked(new(10, 12, 10), new(15, 12, 10)),
            "A cached blocker beyond a new segment endpoint cannot reject coverage.");
        var textured = Wall.Select(t => t with { TextureIndex = 0 }).ToArray();
        var alpha = new byte[] { 128 };
        var alphaQuery = new RatchetPs2.Games.UYA.Occlusion.UyaOcclusionCoverageQuery(textured, [new(1, 1, alpha)], default);
        Check(alphaQuery.Blocked(new(10, 12, 10), new(20, 12, 10)), "An opaque face can become the coherent blocker.");
        alpha[0] = 0;
        Check(!alphaQuery.Blocked(new(10, 12, 10), new(20, 12, 10)), "Coherent blockers still sample alpha for every query.");
        Console.WriteLine($"Dense graph workload: {layered.Length} triangles, {dense.Rays} rays, {dense.QueryTests} query tests in {timer.Elapsed.TotalMilliseconds:F0} ms");
        var threeSeeds = Preview([Room with { Seeds = [Room.Seeds[0], Room.Seeds[0], Room.Seeds[0]] }], layered);
        Check(threeSeeds.Coverage.CoordinateFingerprint == dense.Coverage.CoordinateFingerprint && threeSeeds.Rays > dense.Rays
            && threeSeeds.QueryTests <= dense.QueryTests * 3,
            "Repeated seeds preserve coverage with bounded query work on overlapping geometry.");
    }

    private static OcclusionGraphCoveragePreview Preview(IReadOnlyList<OcclusionCoverageGraph> graphs,
        IReadOnlyList<OcclusionSurfaceTriangle>? triangles = null, IReadOnlyList<OcclusionCoverageShape>? shapes = null,
        IReadOnlyList<OcclusionCoverageCoordinate>? primary = null, IReadOnlyList<OcclusionAlphaTexture>? textures = null)
        => OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, primary ?? [], shapes ?? [], graphs, triangles ?? [], textures ?? []);
    private static OcclusionSurfaceTriangle[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [new(a, b, c, -1, 1), new(a, c, d, -1, 1)];
    private static OcclusionSurfaceTriangle[] Cube(Vector3 lo, Vector3 hi) => [
        ..Quad(new(lo.X,lo.Y,lo.Z),new(lo.X,lo.Y,hi.Z),new(lo.X,hi.Y,hi.Z),new(lo.X,hi.Y,lo.Z)),
        ..Quad(new(hi.X,lo.Y,lo.Z),new(hi.X,hi.Y,lo.Z),new(hi.X,hi.Y,hi.Z),new(hi.X,lo.Y,hi.Z)),
        ..Quad(new(lo.X,lo.Y,lo.Z),new(hi.X,lo.Y,lo.Z),new(hi.X,lo.Y,hi.Z),new(lo.X,lo.Y,hi.Z)),
        ..Quad(new(lo.X,hi.Y,lo.Z),new(lo.X,hi.Y,hi.Z),new(hi.X,hi.Y,hi.Z),new(hi.X,hi.Y,lo.Z)),
        ..Quad(new(lo.X,lo.Y,lo.Z),new(lo.X,hi.Y,lo.Z),new(hi.X,hi.Y,lo.Z),new(hi.X,lo.Y,lo.Z)),
        ..Quad(new(lo.X,lo.Y,hi.Z),new(hi.X,lo.Y,hi.Z),new(hi.X,hi.Y,hi.Z),new(lo.X,hi.Y,hi.Z))];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action, string expected)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or OcclusionCoverageSeedException) { Check(e.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), e.Message); return; }
        throw new Exception($"Expected rejection containing {expected}.");
    }
}
