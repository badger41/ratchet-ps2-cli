using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Base-level bilinear alpha in raw PS2 byte units, without PNG alpha expansion.</summary>
public static class UyaOcclusionAlphaSampling
{
    public static void Validate(IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<OcclusionAlphaTexture> textures)
    {
        if (textures.Count > UyaOcclusionLimits.MaximumTextures
            || textures.Sum(texture => (long)texture.Alpha.Length) > UyaOcclusionLimits.MaximumAlphaBytes)
            throw new InvalidDataException("Occlusion alpha textures exceed the bounded atlas limit.");
        foreach (var texture in textures)
            if (texture.Width is < 1 or > 4096 || texture.Height is < 1 or > 4096
                || (long)texture.Width * texture.Height != texture.Alpha.Length)
                throw new InvalidDataException("Occlusion alpha texture has invalid dimensions or pixels.");
        foreach (var triangle in triangles)
        {
            if (triangle.TextureIndex < -1 || triangle.TextureIndex >= textures.Count || triangle.WrapFlags > 3)
                throw new InvalidDataException("Occlusion triangle has an invalid alpha texture or wrap mode.");
            if (!Finite(triangle.UvA) || !Finite(triangle.UvB) || !Finite(triangle.UvC))
                throw new InvalidDataException("Occlusion triangle has invalid texture coordinates.");
        }
    }

    // Call Validate once at the scene boundary, rather than revalidating the atlas for every ray hit.
    public static double Sample(OcclusionSurfaceTriangle triangle, double u, double v, IReadOnlyList<OcclusionAlphaTexture> textures)
    {
        if (triangle.TextureIndex == -1) return 128; // Authored untextured faces are opaque.
        var texture = textures[triangle.TextureIndex];
        var s = triangle.UvA.X * (1 - u - v) + triangle.UvB.X * u + triangle.UvC.X * v;
        var t = triangle.UvA.Y * (1 - u - v) + triangle.UvB.Y * u + triangle.UvC.Y * v;
        var clampU = (triangle.WrapFlags & 1) != 0; var clampV = (triangle.WrapFlags & 2) != 0;
        var x = (clampU ? Math.Clamp(s, 0, 1) : s - Math.Floor(s)) * texture.Width - .5;
        var y = (clampV ? Math.Clamp(t, 0, 1) : t - Math.Floor(t)) * texture.Height - .5;
        var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y); var fx = x - ix; var fy = y - iy;
        double Pixel(int px, int py) => texture.Alpha[Address(py, texture.Height, clampV) * texture.Width + Address(px, texture.Width, clampU)];
        var a = Pixel(ix, iy); var b = Pixel(ix, iy + 1);
        var top = a + (Pixel(ix + 1, iy) - a) * fx;
        var bottom = b + (Pixel(ix + 1, iy + 1) - b) * fx;
        return top + (bottom - top) * fy;
    }

    private static bool Finite(System.Numerics.Vector2 uv) => float.IsFinite(uv.X) && float.IsFinite(uv.Y)
        && Math.Abs(uv.X) <= 1_000_000 && Math.Abs(uv.Y) <= 1_000_000;

    private static int Address(int value, int size, bool clamp) => clamp ? Math.Clamp(value, 0, size - 1) : ((value % size) + size) % size;
}
