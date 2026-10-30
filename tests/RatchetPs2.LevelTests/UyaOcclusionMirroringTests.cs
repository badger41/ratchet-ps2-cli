using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Occlusion;

internal static class UyaOcclusionMirroringTests
{
    internal static void Run()
    {
        var triangle = new TexturedSurfaceTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY,
            Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 7, true, false);
        UyaInstanceTransform Transform(float x, float y) => new(new(x, 0, 0, 0), new(0, y, 0, 0),
            new(0, 0, 4, 0), new(10, 20, 30, 1));
        foreach (var (x, y) in new[] { (2f, 3f), (-2f, 3f), (-2f, -3f) })
        {
            var result = UyaOcclusionSceneExtractor.TransformSurface(triangle, Transform(x, y));
            var mirrored = x * y < 0;
            Check(result.A == new Vector3(10, 20, 30)
                && result.B == (mirrored ? new Vector3(10, 20 + y, 30) : new Vector3(10 + x, 20, 30))
                && result.C == (mirrored ? new Vector3(10 + x, 20, 30) : new Vector3(10, 20 + y, 30)),
                "World positions must retain the instance transform and reverse only mirrored winding.");
            Check(Vector3.Cross(result.B - result.A, result.C - result.A).Z > 0,
                "Reflection must preserve the authored floor's outward normal.");
            Check(result.MaterialIndex == 7 && result.ClampU && !result.ClampV
                && result.UvA == triangle.UvA && result.UvB == (mirrored ? triangle.UvC : triangle.UvB)
                && result.UvC == (mirrored ? triangle.UvB : triangle.UvC), "UVs and materials must stay attached to their vertices.");
            OcclusionSurfaceTriangle Face(TexturedSurfaceTriangle t) => new(t.A, t.B, t.C, 0, 3)
                { UvA=t.UvA, UvB=t.UvB, UvC=t.UvC, TextureIndex=0, WrapFlags=1 };
            OcclusionAlphaTexture[] textures = [new(2, 2, [0, 64, 128, 255])];
            var before = UyaOcclusionAlphaSampling.Sample(Face(triangle), .2, .3, textures);
            var after = UyaOcclusionAlphaSampling.Sample(Face(result), mirrored ? .3 : .2, mirrored ? .2 : .3, textures);
            Check(Math.Abs(before - after) < .000001, "Winding repair must preserve alpha sampling at the same surface point.");
        }

        // Outpost #10375: 52 opaque world-space faces, with the reflected instances marked in column 10.
        var rows = JsonSerializer.Deserialize<float[][]>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "OcclusionCorner10375.json")))!;
        var faces = rows.Select(row =>
        {
            var reflected = row[10] != 0;
            Vector3 Local(int at) => new(reflected ? -row[at] : row[at], row[at + 1], row[at + 2]);
            var native = new TexturedSurfaceTriangle(Local(0), Local(3), Local(6), default, default, default, 0);
            var transform = new UyaInstanceTransform(new(reflected ? -1 : 1, 0, 0, 0), new(0, 1, 0, 0),
                new(0, 0, 1, 0), new(0, 0, 0, 1));
            var world = UyaOcclusionSceneExtractor.TransformSurface(native, transform);
            return new OcclusionBlockingFace(world.A, world.B, world.C, (uint)row[9]);
        }).ToArray();
        var qualifier = new OcclusionSampleQualifier(faces, (_, _, _) => true);
        var minimum = new Vector3(404, 212, 104);
        Check(faces.Length == 52 && rows.Any(r => r[10] == 0) && rows.Any(r => r[10] != 0), "The fixture must include mirrored and ordinary ramp geometry.");
        for (var step = 0; step <= 5; step++)
        {
            var points = qualifier.Qualify(minimum, centerPull: step * .18f);
            for (var corner = 4; corner < 8; corner++)
                Check(points[corner] == minimum + new Vector3((corner & 1) * 4, ((corner >> 1) & 1) * 4, 4),
                    $"Pull-in {step} moved a clear upper ramp corner underneath the floor.");
            Check(points.All(p => p.X >= 404 && p.X <= 408 && p.Y >= 212 && p.Y <= 216 && p.Z >= 104 && p.Z <= 108),
                "Ramp sample repair must stay inside its octant.");
        }
        Console.WriteLine("PASS mirrored instance winding/UVs and Outpost octant #10375 upper ramp samples");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
