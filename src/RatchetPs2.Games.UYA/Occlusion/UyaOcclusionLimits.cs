using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionLimits
{
    public const int MaximumTriangles = 1_000_000;
    public const int MaximumTextures = 4096;
    public const int MaximumAlphaBytes = 64 * 1024 * 1024;
    public const int AlphaThreshold = 127;
    public const int MaximumCpuRayTests = 40_000_000;
    public const float NearDistance = 0.01f;
    public const float FarDistance = 1000f;
    public const float DepthTolerance = 0.01f;
}

