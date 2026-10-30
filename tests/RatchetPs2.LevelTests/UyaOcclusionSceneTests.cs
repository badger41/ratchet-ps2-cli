using System.Numerics;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionSceneTests
{
    internal static void Run()
    {
        UyaOcclusionOceanTests.Run();
        UyaOcclusionCornerQualificationTests.Run();
        UyaOcclusionMirroringTests.Run();
        CheckSiegeVisibilityPolicy();
        CheckDoubleSidedBlockers();
        CheckBackfaceExitAllowance();
        try { OcclusionSceneBuilder.Build(GameId.DL, []); throw new Exception("Unsupported target accepted."); }
        catch (NotSupportedException) { }
        Console.WriteLine("PASS SDK production occlusion policy and reference calibration");
    }

    private static void CheckDoubleSidedBlockers()
    {
        // Both triangles face upward and retain the SDK's two-sided flag.
        var triangles = new[]
        {
            new OcclusionSurfaceTriangle(new(-2, -2, 2), new(2, -2, 2), new(0, 2, 2), 5, 3),
            new OcclusionSurfaceTriangle(new(-2, -2, 4), new(2, -2, 4), new(0, 2, 4), 6, 3),
        };
        var result = UyaOcclusionRayReference.TraceCalibrated(triangles,
            [(Vector3.Zero, Vector3.UnitZ), (new Vector3(0, 0, 6), -Vector3.UnitZ)], [], [],
            backfaceExitDistance: 1.5f);
        bool Has(int ray, int group) => (result[ray].Geometry[group / 8] & (1 << (group % 8))) != 0;
        if (!Has(0, 5) || Has(0, 6))
            throw new Exception("An opaque back-facing surface must block geometry behind it.");
        if (Has(1, 5) || !Has(1, 6))
            throw new Exception("An opaque front-facing surface must still block geometry behind it.");
    }

    private static void CheckBackfaceExitAllowance()
    {
        OcclusionSurfaceTriangle Face(float z, int group, bool front = false) => front
            ? new(new(-2,-2,z),new(0,2,z),new(2,-2,z),group,3)
            : new(new(-2,-2,z),new(2,-2,z),new(0,2,z),group,3);
        bool Visible(byte[] mask,int group) => (mask[group/8] & (1<<(group%8)))!=0;
        foreach (var front in new[]{false,true}) foreach (var distance in new[]{.5f,1.5f,1.5001f,2f})
        {
            OcclusionSurfaceTriangle[] faces=[Face(distance,5,front),Face(4,6)];
            var result=UyaOcclusionRayReference.TraceCalibrated(faces,[(Vector3.Zero,Vector3.UnitZ)],[],
                [new(-1,7,[0,0,3],.1f)],
                backfaceExitDistance:1.5f)[0];
            var passes=!front && distance<=1.5f;
            if (!Visible(result.Geometry,5) || Visible(result.Geometry,6)!=passes || Visible(result.Mobys,7)!=passes)
                throw new Exception($"Nearby-backface exit failed at distance {distance}, front={front}.");
        }
        var layered=UyaOcclusionRayReference.TraceCalibrated([Face(.5f,5),Face(2,8),Face(4,6)],
            [(Vector3.Zero,Vector3.UnitZ)],[],[],
            backfaceExitDistance:1.5f)[0];
        if (!Visible(layered.Geometry,5) || !Visible(layered.Geometry,8) || Visible(layered.Geometry,6))
            throw new Exception("A farther backface must still block after a nearby exit.");
        var legacy=UyaOcclusionRayReference.TraceCalibrated([Face(.5f,5),Face(4,6)],
            [(Vector3.Zero,Vector3.UnitZ)],[],[])[0];
        if (Visible(legacy.Geometry,6)) throw new Exception("Existing CPU oracle callers must retain their default blocking behavior.");
        foreach (var invalid in new[]{float.NaN,float.PositiveInfinity,-1f,1000.01f})
        {
            try
            {
                UyaOcclusionRayReference.TraceCalibrated([],[],[],[],backfaceExitDistance:invalid);
            }
            catch (InvalidDataException) { continue; }
            throw new Exception("Invalid backface exit distance was accepted.");
        }
    }

    private static void CheckSiegeVisibilityPolicy()
    {
        UyaOcclusionSource Source(string family, int group, int? classId = null, string? fallback = null) =>
            new($"{family}:{group}", family, group, null, null, classId, fallback);
        OcclusionSurfaceTriangle Face(int source, int group, Vector3 a, Vector3 b, Vector3 c) => new(a, b, c, group, 3) { SourceIndex = source };
        var triangles = new[]
        {
            Face(0, 229, new(-2, -2, 2), new(2, -2, 2), new(0, 2, 2)),
            Face(1, 6, new(-2, -2, 4), new(2, -2, 4), new(0, 2, 4)),
            Face(2, 229, new(2, -2, -2), new(2, 2, -2), new(2, 0, 2)),
            Face(3, 7, new(4, -2, -2), new(4, 2, -2), new(4, 0, 2)),
        };
        var sources = new List<UyaOcclusionSource>
        {
            Source("tie", 229, 0x02D6), Source("tfrag", 6), Source("tie", 229, 0x1234), Source("tfrag", 7),
            Source("moby", 8, 0x02D6, "bounds"), Source("shrub", 9, fallback: "missing mesh"),
            Source("tie", 1023, 0x02D5), Source("tie", -1, 0x02D5), Source("tie", 10, 0x1D49),
        };
        // Include variants even without decoded faces, as well as the base center in the ray fixture.
        foreach (var id in new[] { 0x02D5, 0x02D6, 0x1D42, 0x1D43, 0x1D44, 0x1D45, 0x1D46, 0x1D47, 0x1D48,
                     0x1D4C, 0x1D4D, 0x1D4E, 0x1F19, 0x1F1F, 0x1F20, 0x1F21, 0x1F22 })
            sources.Add(Source("tie", 300 + sources.Count, id));
        var (selected, fallback) = UyaOcclusionSceneBuilder.SelectBakeGeometry(triangles, sources);
        if (selected.Count!=triangles.Length || selected[0]!=(triangles[0] with { Flags=2 }) || !selected.Skip(1).SequenceEqual(triangles.Skip(1)))
            throw new Exception("Siege targets must remain rasterized while unrelated blockers keep their flags.");
        var forced = Enumerable.Range(0, 1024).Where(g => (fallback[g / 8] & (1 << (g % 8))) != 0).ToHashSet();
        if (!forced.SetEquals(new[] { 9 }))
            throw new Exception("Only genuinely missing geometry may use the fallback; Siege classes must earn visibility.");
        var rays = new[] { (Vector3.Zero, Vector3.UnitZ), (Vector3.Zero, Vector3.UnitX) };
        var before = UyaOcclusionRayReference.TraceCalibrated(triangles, rays, [], []);
        var after = UyaOcclusionRayReference.TraceCalibrated(selected, rays, [], []);
        bool Visible(byte[] mask, int group) => (mask[group / 8] & (1 << (group % 8))) != 0;
        if (Visible(before[0].Geometry, 6) || !Visible(after[0].Geometry, 6) || !Visible(after[0].Geometry, 229)
            || !Visible(after[1].Geometry, 229) || Visible(after[1].Geometry, 7))
            throw new Exception("Siege targets disappeared, still block rays, or changed ordinary TIE blockers.");
    }
}
