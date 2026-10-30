using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Moby;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

internal static class UyaOcclusionCalibrationTests
{
    internal static OcclusionSurfaceTriangle[] Triangles()
    {
        var a = new OcclusionSurfaceTriangle(new(198, -2, 2), new(200, 2, 2), new(202, -2, 2), 70, 3) { SourceIndex = 44 };
        return [..UyaOcclusionReferenceTests.AlphaFixture(),a,a with {Group=71,SourceIndex=45},
            a with {A=a.A+new Vector3(0,0,.005f),B=a.B+new Vector3(0,0,.005f),C=a.C+new Vector3(0,0,.005f),Group=72,SourceIndex=46}];
    }
    internal static (Vector3 Origin, Vector3 Direction)[] Rays() => [.. UyaOcclusionReferenceTests.AlphaRays(), (new(200, 0, 0), Vector3.UnitZ), (new(200, 0, 6), -Vector3.UnitZ)];
    internal static UyaOcclusionBounds[] Spheres() => [
        new(0,900,[0,0,1],.25f,[0,0,1,.25f],1024,1),new(1,901,[0,0,3],.25f,[0,0,3,.25f],1024,1),
        new(2,902,[0,0,0],.5f,[0,0,0,.5f],1024,1),new(3,903,[0,0,5],.25f,[0,0,5,.25f],1024,1)];
    public static void Run()
    {
        Check(Spheres().Any(s => s.Group == 903), "opaque-wall sphere fixture exists");
        var triangles = Triangles(); var rays = Rays(); var textures = UyaOcclusionReferenceTests.AlphaTextures();
        var tolerantOwner = UyaOcclusionRayReference.TraceCalibrated(triangles, rays, textures, Spheres());
        Check(Set(tolerantOwner[^2].Geometry, 70) && !Set(tolerantOwner[^2].Geometry, 71) && Set(tolerantOwner[^2].Geometry, 72), "tolerant ownership removes exact tie extras while retaining near-depth targets");
        var blocked = UyaOcclusionRayReference.TraceCalibrated([triangles[0] with { TextureIndex = -1 }], [(Vector3.Zero, Vector3.UnitZ)], [], Spheres())[0];
        Check(Set(blocked.Mobys, 900) && !Set(blocked.Mobys, 901) && Set(blocked.Mobys, 902), "sphere targets before wall and inside camera are visible, spheres behind wall are hidden");
        Check(!Set(tolerantOwner[0].Mobys, 903), "moby sphere behind opaque background is hidden");
        Check(Set(tolerantOwner[0].Geometry, 32) && Set(tolerantOwner[0].Mobys, 901), "moby spheres never block background geometry or other mobys");
        Check(!Set(tolerantOwner[23].Mobys, 900), "ray missing a sphere does not include its bit");
        var raw = new byte[0x88]; BinaryPrimitives.WriteInt32LittleEndian(raw, 0x88);
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(0x2c), 2); BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(0x40), 10);
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(0x54), MathF.PI / 2);
        var sphere = UyaOcclusionMobyBounds.Transform(new() { Scale = 1024, BoundingSphere = new() { X = 1, Radius = 3 } }, UyaMobyInstancesReader.ReadInstance(raw), 0, 7);
        Check(Math.Abs(sphere.Center[0] - 10) < .00001 && Math.Abs(sphere.Center[1] - 2) < .00001 && sphere.Radius == 6, "bounds include model units, instance scale, rotation and translation");
        using var header = new MemoryStream(new byte[0x48]); Check(MobyModelReader.ReadHeader(header).BoundingSphere.Radius == 0, "fixed header needs no mesh parsing");
        Console.WriteLine("UYA occlusion calibration checks passed");
    }
    private static bool Set(byte[] mask, int bit) => (mask[bit / 8] & (1 << (bit & 7))) != 0;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
