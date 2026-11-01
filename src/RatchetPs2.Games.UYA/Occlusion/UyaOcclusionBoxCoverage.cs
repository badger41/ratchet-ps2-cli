using RatchetPs2.Core.Occlusion;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Retains the version-one box-only preview and coordinate fingerprint.</summary>
public static class UyaOcclusionBoxCoverage
{
    public static OcclusionCoverageProfile Profile { get; } = UyaOcclusionCoverage.Profile with { AlgorithmVersion = 1 };

    public static OcclusionCoveragePreview Preview(IReadOnlyList<OcclusionCoverageCoordinate> primary,
        IReadOnlyList<OcclusionCoverageBox> boxes, CancellationToken token = default)
        => UyaOcclusionCoverage.Evaluate(primary, boxes, token, "uya-box-coverage-v1\0"u8);
}
