namespace RatchetPs2.Core.Tfrags;

public enum TfragPieceSourceKind
{
    Primary = 1,
    Chunk = 2,
}

public sealed record TfragPiecePayload(
    string SourceArchive,
    TfragPieceSourceKind SourceKind,
    int SourceIndex,
    byte[] Bytes);

public readonly record struct TfragPieceVector2(float X, float Y);

public readonly record struct TfragPieceVector3(float X, float Y, float Z);

public readonly record struct TfragPieceVector4(float X, float Y, float Z, float W);

public sealed record TfragPieceVertex(
    TfragPieceVector3 Position,
    TfragPieceVector3 Normal,
    TfragPieceVector2 TexCoord,
    TfragPieceVector4 Color,
    float LightSelector,
    TfragPieceVector4 LightBaseColor,
    TfragPieceVector3 LightNormal,
    float LightPostScale);

public sealed record TfragPieceMaterial(int TextureId, bool ClampU, bool ClampV);

public sealed record TfragPiecePrimitive(
    TfragPieceMaterial Material,
    IReadOnlyList<TfragPieceVertex> Vertices,
    IReadOnlyList<uint> Indices);

public sealed record TfragPieceLod(
    int LodIndex,
    IReadOnlyList<TfragPiecePrimitive> Primitives);

public sealed record TfragPieceBounds(
    TfragPieceVector3 Minimum,
    TfragPieceVector3 Maximum,
    float Radius);

public sealed record TfragPieceGeometry(
    TfragPieceBounds Bounds,
    IReadOnlyList<int> TextureIds,
    IReadOnlyList<TfragPieceLod> Lods);

public sealed record TfragPieceAsset(
    string ContentId,
    uint CanonicalFormatVersion,
    byte[] CanonicalBytes,
    TfragPieceGeometry Geometry);

public sealed record TfragPieceProvenance(
    string SourceArchive,
    TfragPieceSourceKind SourceKind,
    int SourceIndex,
    int GroupIndex,
    short OcclusionIndex,
    byte OcclusionIndexStash);

public sealed record TfragPieceTransform(
    TfragPieceVector3 Position,
    TfragPieceVector4 Rotation,
    TfragPieceVector3 Scale);

public sealed record TfragPieceDiagnostics(
    int SourceDataLength,
    int SourceVertexCount,
    int SourceTriangleCount,
    int DecodedVertexCount,
    int DecodedTriangleCount);

public sealed record TfragPieceOccurrence(
    string OccurrenceKey,
    string ContentId,
    TfragPieceProvenance Provenance,
    TfragPieceTransform Transform,
    TfragPieceDiagnostics Diagnostics);

public sealed record TfragPieceSource(
    string SourceArchive,
    TfragPieceSourceKind SourceKind,
    int SourceIndex,
    int GroupCount);

public sealed record TfragPieceExtraction(
    IReadOnlyList<TfragPieceAsset> Assets,
    IReadOnlyList<TfragPieceOccurrence> Occurrences,
    IReadOnlyList<TfragPieceSource> Sources);

public sealed record TfragPieceCompositionEdit(
    string Id,
    int GroupIndex,
    byte[] CanonicalBytes,
    TfragPieceTransform Transform);

public sealed record TfragPieceComposition(
    byte[] Bytes,
    int ChangedPieceCount);
