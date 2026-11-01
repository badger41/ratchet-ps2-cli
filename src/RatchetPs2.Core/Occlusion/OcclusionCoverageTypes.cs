using System.Numerics;

namespace RatchetPs2.Core.Occlusion;

public readonly record struct OcclusionCoverageCoordinate(int X, int Y, int Z);
public abstract record OcclusionCoverageShape(Vector3 Center, Quaternion Rotation, bool Exclude);
public sealed record OcclusionCoverageBox(Vector3 Center, Quaternion Rotation, Vector3 Size, bool Exclude = false)
    : OcclusionCoverageShape(Center, Rotation, Exclude);
/// <summary>A circular cylinder centered at the pose, with local Z as its axis.</summary>
public sealed record OcclusionCoverageCylinder(Vector3 Center, Quaternion Rotation, float Radius, float Height, bool Exclude = false)
    : OcclusionCoverageShape(Center, Rotation, Exclude);

/// <summary>Preview limits describe SDK geometry support, not qualified game runtime or native tree capacity.</summary>
public sealed record OcclusionCoverageProfile(
    int AlgorithmVersion, float CellSize, int MinimumCoordinate, int MaximumCoordinate,
    int MaximumSources, int MaximumCells, int MaximumCandidateTests, double BoundaryTolerance);

/// <summary>Source counts follow input order; exclusions count contained cells in the inclusion union, before removal.</summary>
public sealed record OcclusionCoveragePreview(
    IReadOnlyList<OcclusionCoverageCoordinate> Cells, IReadOnlyList<int> SourceCellCounts,
    int CandidateTests, string CoordinateFingerprint);
