using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaTieCollisionGenerator
{
    public const int SurfaceGeneratorVersion = UyaInstancedCollisionSurfaceGenerator.SurfaceGeneratorVersion;
    public const int DefaultMaximumFaces = UyaInstancedCollisionSurfaceGenerator.DefaultMaximumFaces;

    public static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        byte[] tieBytes,
        string additionId,
        int lodIndex,
        byte rawType,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default) => GenerateSurface(
            Read(tieBytes, cancellationToken), additionId, lodIndex, rawType, maximumFaces, cancellationToken);

    public static UyaInstancedCollisionSurfaceCandidate GenerateDecimatedSurface(
        byte[] tieBytes,
        string additionId,
        byte rawType = 0,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default) => GenerateDecimatedSurface(
            Read(tieBytes, cancellationToken), additionId, rawType, maximumFaces, cancellationToken);

    public static UyaInstancedCollisionSurfaceCandidate GenerateDecimatedSurface(
        TieClass tie,
        string additionId,
        byte rawType = 0,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tie);
        var lod = tie.LodTopologies
            .Where(value => value.TriangleCount > 0 && value.UnresolvedLogicalVertexCount == 0)
            .OrderByDescending(value => value.LodIndex)
            .FirstOrDefault()
            ?? throw new InvalidDataException("TIE contains no usable decoded surface LOD.");
        return GenerateSurface(tie, additionId, lod.LodIndex, rawType, maximumFaces, cancellationToken);
    }

    public static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        TieClass tie,
        string additionId,
        int lodIndex,
        byte rawType,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tie);
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = TieSurfaceMeshExtractor.Extract(tie, lodIndex);
        return UyaInstancedCollisionSurfaceGenerator.GenerateSurface(
            mesh.Positions,
            mesh.Triangles.Select(value => (value.A, value.B, value.C)).ToArray(),
            additionId,
            lodIndex,
            $"TIE LOD {lodIndex}",
            rawType,
            maximumFaces,
            cancellationToken);
    }

    private static TieClass Read(byte[] tieBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tieBytes);
        if (tieBytes.Length == 0 || tieBytes.Length > UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes)
            throw new ArgumentException(
                $"UYA TIE input must contain 1 through {UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes} bytes.",
                nameof(tieBytes));
        cancellationToken.ThrowIfCancellationRequested();
        return TieClassReader.Read(
            tieBytes,
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
    }
}
