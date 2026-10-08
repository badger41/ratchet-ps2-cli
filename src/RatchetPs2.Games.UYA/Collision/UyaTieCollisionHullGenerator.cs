using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaTieCollisionHullGenerator
{
    public const int GeneratorVersion = UyaInstancedCollisionHullGenerator.GeneratorVersion;
    public const int DefaultProfileSections = UyaInstancedCollisionHullGenerator.DefaultProfileSections;
    public const int DefaultMaximumFaces = UyaInstancedCollisionHullGenerator.DefaultMaximumFaces;
    public const int MaximumProfileSections = UyaInstancedCollisionHullGenerator.MaximumProfileSections;

    public static UyaInstancedCollisionHullCandidate Generate(
        byte[] tieBytes,
        string additionId,
        int lodIndex = 0,
        byte rawType = 0,
        int profileSections = DefaultProfileSections,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default) => Generate(
            Read(tieBytes, cancellationToken), additionId, lodIndex, rawType,
            profileSections, maximumFaces, cancellationToken);

    public static UyaInstancedCollisionHullCandidate Generate(
        TieClass tie,
        string additionId,
        int lodIndex = 0,
        byte rawType = 0,
        int profileSections = DefaultProfileSections,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tie);
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = TieSurfaceMeshExtractor.Extract(tie, lodIndex);
        return UyaInstancedCollisionHullGenerator.Generate(
            mesh.Positions,
            mesh.Triangles.Select(value => (value.A, value.B, value.C)).ToArray(),
            additionId,
            lodIndex,
            $"TIE LOD {lodIndex}",
            rawType,
            profileSections,
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
