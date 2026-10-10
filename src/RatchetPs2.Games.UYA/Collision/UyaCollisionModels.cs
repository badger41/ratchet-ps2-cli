using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

public readonly record struct UyaCollisionVertex(int X64, int Y64, int Z64)
{
    public Vector3 Position => new(X64 / 64f, Y64 / 64f, Z64 / 64f);
}

public readonly record struct UyaCollisionSolidFace(
    byte Type,
    UyaCollisionVertex A,
    UyaCollisionVertex B,
    UyaCollisionVertex C,
    UyaCollisionVertex D,
    bool IsQuad)
{
    public int CollisionType => Type & 0x0f;

    public int SoundType => Type >> 4;
}

public sealed record UyaCollisionSolidPiece(
    int SourceIndex,
    IReadOnlyList<UyaCollisionSolidFace> Faces);

public readonly record struct UyaCollisionTriangle(byte A, byte B, byte C);

public readonly record struct UyaCollisionSphere(UyaCollisionVertex Center, int Radius64)
{
    public Vector4 Value => new(
        Center.Position.X,
        Center.Position.Y,
        Center.Position.Z,
        Radius64 / 64f);
}

public sealed record UyaCollisionPlayerBarrier(
    int SourceIndex,
    UyaCollisionSphere BoundingSphere,
    IReadOnlyList<UyaCollisionVertex> Vertices,
    IReadOnlyList<UyaCollisionTriangle> Triangles);

public sealed record UyaMapCollision(
    IReadOnlyList<UyaCollisionSolidPiece> SolidPieces,
    IReadOnlyList<UyaCollisionPlayerBarrier> PlayerBarriers,
    int NativeOctantCount,
    int NativeFaceCount,
    int DuplicateSolidFaceCount);

public enum UyaCollisionPieceKind
{
    Solid = 1,
    PlayerBarrier = 2,
}

public readonly record struct UyaCollisionPieceEdit(
    UyaCollisionPieceKind Kind,
    int SourcePieceIndex,
    int TranslationX64,
    int TranslationY64,
    int TranslationZ64,
    bool Remove = false,
    Quaternion Rotation = default);

public sealed record UyaCollisionSolidAddition(
    string Id,
    IReadOnlyList<UyaCollisionSolidFace> Faces);

public readonly record struct UyaCollisionInstanceTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale);

public sealed record UyaCollisionInstance(string Id, UyaCollisionInstanceTransform Transform);

public sealed record UyaTieCollisionGroup(
    byte[] TieBytes,
    IReadOnlyList<UyaCollisionInstance> Instances);

public sealed record UyaShrubCollisionGroup(
    byte[] ShrubBytes,
    IReadOnlyList<UyaCollisionInstance> Instances);

public sealed record UyaCollisionPieceCandidate(
    string InstanceId,
    int SourcePieceIndex,
    float Confidence);

public sealed record UyaCollisionOctantCost(
    int X,
    int Y,
    int Z,
    int FaceCount,
    int VertexCount,
    int QuadCount,
    int EncodedByteCount,
    IReadOnlyList<string> AdditionIds,
    IReadOnlyList<string> Violations);

public sealed record UyaCollisionAnalysis(
    int LogicalFaceCount,
    int LogicalVertexCount,
    int OccupiedOctantCount,
    int DuplicateFaceCount,
    int HardViolationCount,
    IReadOnlyList<UyaCollisionOctantCost> Octants);

public sealed record UyaCollisionComposition(
    byte[] Bytes,
    bool Changed,
    IReadOnlyList<UyaCollisionPieceEdit> EffectiveEdits,
    UyaCollisionAnalysis? Analysis = null);
