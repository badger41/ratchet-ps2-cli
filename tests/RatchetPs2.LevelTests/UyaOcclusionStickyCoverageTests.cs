using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionStickyCoverageTests
{
    private static readonly OcclusionCoverageGraph Room = new(new(16), Quaternion.Identity, new(23), [new(-6, -3.7f, -4.3f)]) { StickyDistance = 12 };
    private static readonly OcclusionSurfaceTriangle[] Floor = Quad(new(-100,-100,0), new(100,-100,0), new(100,100,0), new(-100,100,0));

    internal static void Run()
    {
        var near = Preview(Room, Floor);
        Check(near.Graphs[0].Accepted.Count == 72 && near.Graphs[0].Rejected.Count == 144
            && near.Graphs[0].Accepted.All(c => c.Z is 1 or 2), "Sticky removes air cells above the surface-search distance.");
        Check(Preview(Room with { StickyDistance = 4 }, Floor).Coverage.Cells.Count == 0, "A smaller distance retains fewer layers.");
        Check(Preview(Room with { StickyDistance = null }, []).Coverage.Cells.Count == 216, "Disabled Sticky preserves old graph behavior.");
        Check(Preview(Room, []).Coverage.Cells.Count == 0, "No explicit surfaces means no sticky octants.");
        Check(Preview(Room, Floor.Select(t => t with { A = t.C, C = t.A }).ToArray()).Coverage.Cells.Count == 0,
            "Back-facing undersides do not retain air above them.");
        foreach (var alpha in new byte[] { 0, 127, 128 })
            Check(Preview(Room, Floor.Select(t => t with { TextureIndex = 0 }).ToArray(), [new(1,1,[alpha])]).Coverage.Cells.Count == (alpha == 128 ? 72 : 0),
                "Sticky uses the existing material alpha threshold at every hit.");
        Check(Preview(Room, Floor.Select(t => t with { Flags = 0 }).ToArray()).Coverage.Cells.Count == 0,
            "Nonblocking geometry does not become a sticky surface.");
        var shared = OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [Room], Floor, [], stickyTriangles: Floor);
        var separate = OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [Room], Floor, [], stickyTriangles: Floor.ToArray());
        Check(shared.Coverage.CoordinateFingerprint == separate.Coverage.CoordinateFingerprint
            && shared.Rays == separate.Rays && shared.QueryTests == separate.QueryTests,
            "Sharing the Sticky/visibility index preserves coverage, ray order and the shared work budget.");
        var floorQuery = new UyaOcclusionCoverageQuery(Floor, [], default);
        Check(floorQuery.NearSurface(new(10,12,12),12) && !floorQuery.NearSurface(new(10,12,12.01),12),
            "The search includes its finite endpoint and excludes farther surfaces.");
        var diagonal = new UyaOcclusionCoverageQuery(Quad(new(14,13,13), new(14,13,15), new(14,15,15), new(14,15,13)), [], default);
        Check(diagonal.NearSurface(new(10,10,10),7) && !diagonal.NearSurface(new(10,10,10),6),
            "The v1 corner directions retain diagonal-only nearby surfaces.");
        var offset = new UyaOcclusionCoverageQuery(Floor.Select(t => t with { A = t.A + new Vector3(0,0,2), B = t.B + new Vector3(0,0,2), C = t.C + new Vector3(0,0,2) }).ToArray(), [], default);
        Check(offset.NearSurface(new(10,12,1),0), "The v1 two-unit starting offset includes a surface crossing the octant itself.");
        var rescue = new OcclusionCoverageBox(new(22,22,26), Quaternion.Identity, Vector3.One);
        var exclude = new OcclusionCoverageBox(new(10,10,10), Quaternion.Identity, new(4.1f), true);
        var combined = OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [rescue,exclude], [Room], [], [], stickyTriangles: Floor);
        Check(combined.Coverage.Cells.Contains(new(5,5,6)) && !combined.Coverage.Cells.Contains(new(2,2,2)),
            "Include shapes can rescue sticky-rejected cells and exclusions still apply last.");
        foreach (var distance in new[] { -1f, float.NaN, float.PositiveInfinity, 1_000_001f })
            Throws<InvalidDataException>(() => Preview(Room with { StickyDistance = distance }, Floor));
        Throws<InvalidDataException>(() => OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [Room], [], []));
        var visibility = new UyaOcclusionCoverageQuery([], [], default);
        var sticky = new UyaOcclusionCoverageQuery(Floor, [], default, visibility.WorkBudget);
        visibility.WorkBudget.Rays = UyaOcclusionGraphCoverage.Profile.MaximumRays;
        Throws<InvalidDataException>(() => sticky.NearSurface(new(10,12,10),12));
        using var cancellation = new CancellationTokenSource();
        var cancelled = new UyaOcclusionCoverageQuery(Floor, [], cancellation.Token); cancellation.Cancel();
        Throws<OperationCanceledException>(() => cancelled.NearSurface(new(10,12,10),12));
        Console.WriteLine("PASS Sticky graph coverage: v1 axes/corners/offsets, finite distance, winding, alpha, composition, shared budget and cancellation.");
    }
    private static OcclusionGraphCoveragePreview Preview(OcclusionCoverageGraph graph, IReadOnlyList<OcclusionSurfaceTriangle> surfaces,
        IReadOnlyList<OcclusionAlphaTexture>? textures = null) => OcclusionCoverageBuilder.PreviewGraphs(GameId.UYA, [], [], [graph], [], textures ?? [], stickyTriangles: surfaces);
    private static OcclusionSurfaceTriangle[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [new(a,b,c,-1,1),new(a,c,d,-1,1)];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}.");
    }
}
