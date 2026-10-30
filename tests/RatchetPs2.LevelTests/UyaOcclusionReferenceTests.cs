using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Numerics;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.UYA.Level;

internal static class UyaOcclusionReferenceTests
{
    internal static OcclusionSurfaceTriangle[] Fixture() => [
        new(new(-2,-2,2), new(0,2,2), new(2,-2,2), 0, 1), // front-facing opaque wall
        new(new(-2,-2,4), new(0,2,4), new(2,-2,4), 31, 0), // hidden behind wall
        new(new(-2,-2,1), new(0,2,1), new(2,-2,1), 1023, 0), // transparent foreground
        new(new(5,0,0), new(5,2,0), new(5,0,2), 32, 1)];
    internal static (Vector3 Origin, Vector3 Direction)[] Rays() => [
        (Vector3.Zero, Vector3.UnitZ), (new(0,0,6), -Vector3.UnitZ),
        (new(20,20,20), Vector3.UnitX), (Vector3.Zero, Vector3.UnitY),
        (new(-2,-2,0), Vector3.UnitZ), (Vector3.Zero, Vector3.UnitX)];
    internal static OcclusionAlphaTexture[] AlphaTextures() => [new(1, 1, [126]), new(1, 1, [127]), new(1, 1, [128]), new(1, 1, [0]), new(2, 1, [0, 128]), new(2, 1, [126, 128]), new(2, 2, [0, 128, 126, 127])];
    internal static OcclusionSurfaceTriangle[] AlphaFixture()
    {
        var result = new List<OcclusionSurfaceTriangle>();
        var settings = new (int Texture, float U, uint Wrap)[] { (0,.5f,0),(1,.5f,0),(2,.5f,0),(3,.5f,0),
            (4,.25f,0),(4,.75f,0),(4,1.25f,0),(4,1.25f,1),(4,-.25f,0),(4,-.25f,1),
            (4,.5f,0),(4,.75f,0),(4,.75f,0),(5,.5f,0),(5,.375f,0),(2,.5f,0),(6,.75f,0),(6,.25f,0),(6,.25f,2),(6,.75f,0),(6,.75f,2),(6,.5f,0) };
        for (var i = 0; i < settings.Length; i++)
        {
            var (texture, u, wrap) = settings[i]; var x = i * 8;
            var front = new OcclusionSurfaceTriangle(new(x - 2, -2, 2), new(x, 2, 2), new(x + 2, -2, 2), i == 11 ? -1 : i, 3)
            { TextureIndex = texture, UvA = new(u, .5f), UvB = new(u, .5f), UvC = new(u, .5f), WrapFlags = wrap };
            if (i == 12) front = front with { UvA = new(0, 0), UvB = new(1, .5f), UvC = new(1, 1) };
            if (i is 16 or 17 or 18 or 19 or 20 or 21)
            {
                var t = i == 16 ? .75f : i is 17 or 18 ? -.25f : i is 19 or 20 ? 1.25f : .5f;
                front = front with { UvA = new(u, t), UvB = new(u, t), UvC = new(u, t) };
            }
            result.Add(front);
            result.Add(new(new(x - 2, -2, 4), new(x, 2, 4), new(x + 2, -2, 4), 32 + i, 3));
        }
        return result.ToArray();
    }
    internal static (Vector3 Origin, Vector3 Direction)[] AlphaRays() => Enumerable.Range(0, 22)
        .Select(i => (new Vector3(i * 8, 0, 0), Vector3.UnitZ))
        .Concat([(new Vector3(120, 0, 6), -Vector3.UnitZ), (new Vector3(-100, 0, 0), Vector3.UnitZ), (Vector3.Zero, Vector3.UnitY)]).ToArray();

    public static void Run()
    {
        UyaOcclusionSurfaceTests.Run();
        VerifyAlpha();
        var fixture = Fixture(); var rays = Rays();
        var result = UyaOcclusionRayReference.Trace(fixture, rays);
        Check(Set(result[0]).SequenceEqual([0, 1023]), "opaque wall blocks background while transparent foreground remains visible");
        Check(Set(result[1]).SequenceEqual([0, 31, 1023]), "back face does not block");
        Check(!result[2].Any(b => b != 0) && !result[3].Any(b => b != 0), "misses and parallel directions");
        Check(Set(result[4]).SequenceEqual([0, 1023]), "edge inclusion");
        Check(Set(result[5]).SequenceEqual([32]), "visibility crosses a 32-bit word boundary");
        var twoSided = fixture.ToArray(); twoSided[0] = twoSided[0] with { Flags = 3 };
        var reverse = UyaOcclusionRayReference.Trace(twoSided, [rays[1]])[0];
        Check(Set(reverse).SequenceEqual([0, 31]), "two-sided blockers hide the transparent target behind them");
        Throws<InvalidDataException>(() => UyaOcclusionRayReference.Trace(fixture, [(Vector3.Zero, Vector3.One)]));
        Throws<OperationCanceledException>(() => UyaOcclusionRayReference.Trace(fixture, rays, new CancellationToken(true)));
        Throws<InvalidDataException>(() => UyaOcclusionRayReference.Trace(fixture, Enumerable.Repeat(rays[0], 257).ToArray()));
        Console.WriteLine("UYA occlusion reference checks passed");
    }
    private static void VerifyAlpha()
    {
        var triangles = AlphaFixture(); var textures = AlphaTextures();
        var masks = UyaOcclusionRayReference.Trace(triangles, AlphaRays(), textures);
        int[][] expected = [[0, 32], [1], [2], [35], [36], [5], [38], [7], [8], [41], [10, 42], [], [12], [13], [14, 46], [15], [16], [17, 49], [50], [19], [20], [21, 53], [47], [], []];
        for (var i = 0; i < expected.Length; i++) Check(Set(masks[i]).SequenceEqual(expected[i]), $"alpha threshold/UV/addressing case {i}");
        Throws<InvalidDataException>(() => UyaOcclusionRayReference.Trace(triangles, AlphaRays(), []));
        Throws<InvalidDataException>(() => UyaOcclusionRayReference.Trace(triangles, AlphaRays(), [new(2, 2, [0])]));
        Throws<InvalidDataException>(() => UyaOcclusionRayReference.Trace([triangles[0] with { UvA = new(float.NaN) }], AlphaRays(), textures));
        // A palette ID advances 0x100 bytes; preserve raw alpha rather than PNG-style expansion.
        var palette = new byte[0x500]; palette[0x100 + 3] = 126; palette[0x100 + 7] = 127; palette[0x100 + 11] = 128; palette[0x100 + 15] = 255;
        var definition = new LevelAssetTextureDefinition(0, 0, 2, 2, 1, 1, -1, 0);
        var asset = LevelAssetReader.BuildAssetTexture("test", 0, definition, palette, [0, 1, 2, 3], 0, isSwizzled: false);
        var image = TextureConverter.Decode(PifReader.Read(asset.PifBytes), new TextureConversionOptions { DoubleAlpha = false });
        Check(Enumerable.Range(0, 4).Select(i => image.PixelData[i * 4 + 3]).SequenceEqual(new byte[] { 126, 127, 128, 255 }), "SDK palette stride and raw alpha units");
    }

    private static IEnumerable<int> Set(byte[] bytes) => Enumerable.Range(0, 1024).Where(bit => (bytes[bit / 8] & (1 << (bit & 7))) != 0);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}"); }
}
