using RatchetPs2.Core.Occlusion;

namespace RatchetPs2.Games.UYA.Occlusion;

public sealed record UyaOcclusionSource(string Key, string Family, int Group, int? MappingIndex,
    int? MappingSecondWord, int? ClassId, string? Fallback);

public sealed record UyaOcclusionBounds(int SourceIndex, int Group, float[] Center, float Radius,
    float[] LocalSphere, float ModelScale, float InstanceScale) : OcclusionTargetSphere(SourceIndex, Group, Center, Radius);

internal sealed record UyaOcclusionExtractedScene(
    IReadOnlyList<OcclusionSurfaceTriangle> Triangles, IReadOnlyList<OcclusionAlphaTexture> Textures,
    IReadOnlyList<UyaOcclusionBounds> MobySpheres, IReadOnlyList<UyaOcclusionSource> Sources,
    byte[] Grid, IReadOnlyList<float> BarrierHeights);
