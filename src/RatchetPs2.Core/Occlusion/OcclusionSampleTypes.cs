namespace RatchetPs2.Core.Occlusion;

public sealed record OcclusionCell(int X, int Y, int Z, bool ReservedVisible, IReadOnlyList<int> Samples, byte InwardCorners = 0);
public readonly record struct OcclusionOctant(int X, int Y, int Z, bool ReservedVisible);
