using System.Numerics;

namespace RatchetPs2.Core.Occlusion;

public readonly record struct OcclusionSurfaceTriangle(Vector3 A, Vector3 B, Vector3 C, int Group, uint Flags)
{
    public int SourceIndex { get; init; } = -1;
    public int SourceFaceIndex { get; init; } = -1;
    public Vector2 UvA { get; init; }
    public Vector2 UvB { get; init; }
    public Vector2 UvC { get; init; }
    public int TextureIndex { get; init; } = -1;
    public uint WrapFlags { get; init; }
}
public sealed record OcclusionAlphaTexture(int Width, int Height, byte[] Alpha)
{
    public string Family { get; init; } = "fixture";
    public int SourceTextureIndex { get; init; } = -1;
}
public record OcclusionTargetSphere(int SourceIndex, int Group, float[] Center, float Radius);
public sealed record OcclusionReferenceMasks(byte[] Geometry, byte[] Mobys);
public sealed record OcclusionSceneGeometry(
    IReadOnlyList<OcclusionSurfaceTriangle> Triangles, IReadOnlyList<OcclusionAlphaTexture> Textures,
    IReadOnlyList<OcclusionTargetSphere> Spheres, IReadOnlyList<OcclusionOctant> Octants,
    byte[] OriginalGrid, byte[] FallbackMask, IReadOnlyList<float> BarrierHeights);
