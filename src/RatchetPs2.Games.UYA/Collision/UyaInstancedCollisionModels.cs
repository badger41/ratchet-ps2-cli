namespace RatchetPs2.Games.UYA.Collision;

public sealed record UyaInstancedCollisionSurfaceCandidate(
    UyaCollisionSolidAddition Addition,
    int GeneratorVersion,
    int LodIndex,
    byte RawType,
    int MaximumFaces,
    int SourceVertexCount,
    int SourceTriangleCount,
    int GeneratedVertexCount,
    int GeneratedFaceCount,
    int MergedQuadCount,
    int RemovedDegenerateFaceCount,
    int RemovedDuplicateFaceCount,
    float MaximumVertexDeviation,
    UyaCollisionAnalysis Analysis);

public sealed record UyaInstancedCollisionHullCandidate(
    UyaCollisionSolidAddition Addition,
    int GeneratorVersion,
    int LodIndex,
    byte RawType,
    int MaximumFaces,
    int ProfileSections,
    int SourceVertexCount,
    int SourceTriangleCount,
    int GeneratedVertexCount,
    int GeneratedFaceCount,
    int MergedQuadCount,
    int DeviationSampleCount,
    float MaximumSourceVertexDeviation,
    UyaCollisionAnalysis Analysis);
