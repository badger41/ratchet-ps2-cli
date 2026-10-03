using System.Numerics;

namespace RatchetPs2.Sdk;

public sealed record CollisionGltfFiles(byte[] GltfBytes, byte[] BinBytes);

public sealed record CollisionGltfPalette(
    IReadOnlyList<Vector3> CollisionTypeSrgbColors,
    IReadOnlyList<Vector3> SoundTypeSrgbColors,
    Vector3 PlayerBarrierSrgbColor);

public enum CollisionPieceKind
{
    Solid = 1,
    PlayerBarrier = 2,
}

public readonly record struct CollisionPieceEdit(
    CollisionPieceKind Kind,
    int SourcePieceIndex,
    float TranslationX,
    float TranslationY,
    float TranslationZ,
    bool Remove = false);

public readonly record struct CollisionVertex(float X, float Y, float Z);

public readonly record struct CollisionSolidFace(
    byte RawType,
    CollisionVertex A,
    CollisionVertex B,
    CollisionVertex C,
    CollisionVertex D,
    bool IsQuad);

public sealed record CollisionSolidAddition(string Id, IReadOnlyList<CollisionSolidFace> Faces);

public readonly record struct CollisionRotation(float X, float Y, float Z, float W);

public readonly record struct CollisionInstanceTransform(
    CollisionVertex Position,
    CollisionRotation Rotation,
    CollisionVertex Scale);

public enum TieCollisionCandidateKind
{
    Surface = 1,
    ConvexHull = 2,
}

public sealed record TieCollisionRecipe(
    TieCollisionCandidateKind Kind,
    int Version,
    int LodIndex,
    byte RawType,
    int MaximumFaces,
    int ProfileSections = 0);

public sealed record TieCollisionCandidate(
    TieCollisionRecipe Recipe,
    CollisionSolidAddition Addition,
    int SourceVertexCount,
    int SourceTriangleCount,
    int GeneratedVertexCount,
    int GeneratedFaceCount,
    int MergedQuadCount,
    int RemovedDegenerateFaceCount,
    int RemovedDuplicateFaceCount,
    float MaximumVertexDeviation,
    CollisionAnalysis Analysis);

public sealed record CollisionOctantCost(
    int X,
    int Y,
    int Z,
    int FaceCount,
    int VertexCount,
    int QuadCount,
    int EncodedByteCount,
    IReadOnlyList<string> AdditionIds,
    IReadOnlyList<string> Violations);

public sealed record CollisionAnalysis(
    int LogicalFaceCount,
    int LogicalVertexCount,
    int OccupiedOctantCount,
    int DuplicateFaceCount,
    int HardViolationCount,
    IReadOnlyList<CollisionOctantCost> Octants);

public sealed record CollisionComposition(
    byte[] Bytes,
    bool Changed,
    IReadOnlyList<CollisionPieceEdit> EffectiveEdits,
    CollisionAnalysis? Analysis = null);

public readonly record struct CollisionTypeCount(byte RawType, int Count);

public sealed record CollisionPieceInfo(
    CollisionPieceKind Kind,
    int SourcePieceIndex,
    int FaceCount,
    int VertexCount,
    IReadOnlyList<CollisionTypeCount> Types);

public sealed record CollisionInspection(IReadOnlyList<CollisionPieceInfo> Pieces);
