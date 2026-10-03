using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Collision;

namespace RatchetPs2.Sdk;

public static class CollisionWork
{
    public const int DefaultMaximumFaces = 100_000;
    public const int DefaultProfileSections = 6;

    public static TieCollisionCandidate GenerateTieSurfaceCandidate(
        byte[] tieBytes,
        GameId gameId,
        string additionId,
        int lodIndex = 0,
        byte rawType = 0,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tieBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromCandidate(UyaTieCollisionGenerator.GenerateSurface(
                tieBytes, additionId, lodIndex, rawType, maximumFaces, cancellationToken)),
            _ => throw new NotSupportedException($"Tie collision generation does not support {gameId}."),
        };
    }

    public static TieCollisionCandidate GenerateTieDecimatedCandidate(
        byte[] tieBytes,
        GameId gameId,
        string additionId,
        byte rawType = 0,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tieBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromCandidate(UyaTieCollisionGenerator.GenerateDecimatedSurface(
                tieBytes, additionId, rawType, maximumFaces, cancellationToken)),
            _ => throw new NotSupportedException($"Tie collision decimation does not support {gameId}."),
        };
    }

    public static TieCollisionCandidate GenerateTieConvexHullCandidate(
        byte[] tieBytes,
        GameId gameId,
        string additionId,
        int lodIndex = 0,
        byte rawType = 0,
        int profileSections = DefaultProfileSections,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tieBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromCandidate(UyaTieCollisionHullGenerator.Generate(
                tieBytes, additionId, lodIndex, rawType, profileSections, maximumFaces, cancellationToken)),
            _ => throw new NotSupportedException($"Tie collision convex hull generation does not support {gameId}."),
        };
    }

    public static CollisionComposition EncodeStandalone(
        GameId gameId,
        IReadOnlyList<CollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additions);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromComposition(UyaCollisionComposer.EncodeStandalone(
                additions.Select(UyaCollisionAdapter.ToAddition).ToArray(), cancellationToken)),
            _ => throw new NotSupportedException($"Standalone collision encoding does not support {gameId}."),
        };
    }

    public static CollisionSolidAddition TransformAddition(
        CollisionSolidAddition addition,
        GameId gameId,
        string additionId,
        CollisionInstanceTransform transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addition);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromAddition(UyaCollisionAdditionTransformer.Transform(
                UyaCollisionAdapter.ToAddition(addition),
                additionId,
                new(transform.Position.X, transform.Position.Y, transform.Position.Z),
                new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
                new(transform.Scale.X, transform.Scale.Y, transform.Scale.Z),
                cancellationToken)),
            _ => throw new NotSupportedException($"Collision addition transforms do not support {gameId}."),
        };
    }

    public static CollisionSolidAddition DecodeSolidAddition(
        byte[] collisionBytes,
        GameId gameId,
        string additionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var result = gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromAddition(new(
                additionId,
                UyaCollisionReader.Read(collisionBytes).SolidPieces.SelectMany(piece => piece.Faces).ToArray())),
            _ => throw new NotSupportedException($"Collision solid decoding does not support {gameId}."),
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Faces.Count == 0)
            throw new InvalidDataException("Collision proxy contains no solid faces.");
        return result;
    }

    public static CollisionAnalysis AnalyzeComposition(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits,
        IReadOnlyList<CollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(additions);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromAnalysis(UyaCollisionComposer.AnalyzeComposition(
                collisionBytes,
                UyaCollisionAdapter.ToEdits(edits),
                additions.Select(UyaCollisionAdapter.ToAddition).ToArray(),
                cancellationToken)),
            _ => throw new NotSupportedException($"Collision composition analysis does not support {gameId}."),
        };
    }

    public static CollisionComposition Compose(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits,
        IReadOnlyList<CollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(additions);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromComposition(UyaCollisionComposer.Compose(
                collisionBytes,
                UyaCollisionAdapter.ToEdits(edits),
                additions.Select(UyaCollisionAdapter.ToAddition).ToArray(),
                cancellationToken)),
            _ => throw new NotSupportedException($"Collision composition does not support {gameId}."),
        };
    }

}
