using System.Numerics;

namespace RatchetPs2.Core.Occlusion;

/// <summary>A bounded candidate region with owned seeds in region-local coordinates.</summary>
public sealed record OcclusionCoverageGraph(Vector3 Center, Quaternion Rotation, Vector3 Size, IReadOnlyList<Vector3> Seeds)
{
    /// <summary>Null disables the v1-style surface proximity filter. Distance is in native map units.</summary>
    public float? StickyDistance { get; init; }
    /// <summary>Optional circular local-Z boundary. Size X/Y must equal twice this radius; Size Z is height.</summary>
    public float? CylinderRadius { get; init; }
}
public sealed record OcclusionGraphCoverageProfile(int AlgorithmVersion, int MaximumSeedsPerGraph,
    int MaximumSeeds, int MaximumGraphCandidates, int MaximumRays, int MaximumQueryTests,
    double ProbeInset, double EndpointClearance);
/// <summary>Lists follow graph input order; accepted/rejected coordinates are canonical and precede final exclusions.</summary>
public sealed record OcclusionGraphCoverageResult(IReadOnlyList<OcclusionCoverageCoordinate> Accepted,
    IReadOnlyList<OcclusionCoverageCoordinate> Rejected);
public sealed record OcclusionGraphCoveragePreview(OcclusionCoveragePreview Coverage,
    IReadOnlyList<OcclusionGraphCoverageResult> Graphs, int Rays, int QueryTests);

/// <summary>Calibrated blocker geometry independent of existing visibility groups or octants.</summary>
public sealed record OcclusionCoverageGeometry(IReadOnlyList<OcclusionSurfaceTriangle> Triangles, IReadOnlyList<OcclusionAlphaTexture> Textures)
{
    /// <summary>Optional current-scene TIE/tfrag surfaces, using the same alpha atlas as Triangles.</summary>
    public IReadOnlyList<OcclusionSurfaceTriangle>? StickyTriangles { get; init; }
}
