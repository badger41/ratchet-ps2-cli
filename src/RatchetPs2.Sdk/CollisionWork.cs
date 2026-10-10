using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Collision;

namespace RatchetPs2.Sdk;

public static class CollisionWork
{
    public const int DefaultMaximumFaces = 100_000;
    public const int DefaultProfileSections = 6;

    public static InstancedCollisionCandidate GenerateTieSurfaceCandidate(
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

    public static InstancedCollisionCandidate GenerateTieDecimatedCandidate(
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

    public static InstancedCollisionCandidate GenerateTieConvexHullCandidate(
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

    public static InstancedCollisionCandidate GenerateShrubSurfaceCandidate(
        byte[] shrubBytes,
        GameId gameId,
        string additionId,
        byte rawType = 0,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shrubBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromCandidate(UyaShrubCollisionGenerator.GenerateSurface(
                shrubBytes, additionId, rawType, maximumFaces, cancellationToken)),
            _ => throw new NotSupportedException($"Shrub collision generation does not support {gameId}."),
        };
    }

    public static InstancedCollisionCandidate GenerateShrubConvexHullCandidate(
        byte[] shrubBytes,
        GameId gameId,
        string additionId,
        byte rawType = 0,
        int profileSections = DefaultProfileSections,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shrubBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromCandidate(UyaShrubCollisionGenerator.GenerateHull(
                shrubBytes, additionId, rawType, profileSections, maximumFaces, cancellationToken)),
            _ => throw new NotSupportedException($"Shrub collision convex hull generation does not support {gameId}."),
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

    public static CollisionSolidAddition TransformAdditionRelative(
        CollisionSolidAddition addition,
        GameId gameId,
        string additionId,
        CollisionInstanceTransform editTransform,
        CollisionInstanceTransform sourceParentTransform,
        CollisionInstanceTransform targetParentTransform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addition);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.FromAddition(UyaCollisionAdditionTransformer.TransformRelative(
                UyaCollisionAdapter.ToAddition(addition),
                additionId,
                UyaCollisionAdapter.ToTransform(editTransform),
                UyaCollisionAdapter.ToTransform(sourceParentTransform),
                UyaCollisionAdapter.ToTransform(targetParentTransform),
                cancellationToken)),
            _ => throw new NotSupportedException($"Relative collision transforms do not support {gameId}."),
        };
    }

    public static CollisionSolidAddition DecodeSolidPieceAddition(
        byte[] collisionBytes,
        GameId gameId,
        int sourcePieceIndex,
        string additionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        if (sourcePieceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourcePieceIndex));
        UyaInstancedCollisionSurfaceGenerator.ValidateAdditionId(additionId);
        var piece = DecodeSolidPieces(collisionBytes, gameId, cancellationToken)
            .SingleOrDefault(value => value.SourcePieceIndex == sourcePieceIndex)
            ?? throw new ArgumentOutOfRangeException(
                nameof(sourcePieceIndex), sourcePieceIndex, "Solid collision piece does not exist.");
        return new(additionId, piece.Faces);
    }

    public static IReadOnlyList<CollisionSolidPiece> DecodeSolidPieces(
        byte[] collisionBytes,
        GameId gameId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var result = gameId switch
        {
            GameId.UYA => UyaCollisionReader.Read(collisionBytes).SolidPieces
                .Select(UyaCollisionAdapter.FromPiece).ToArray(),
            _ => throw new NotSupportedException($"Collision piece extraction does not support {gameId}."),
        };
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static IReadOnlyList<CollisionInstancePieceCandidate> FindTieCollisionCandidates(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionTieGroup> groups,
        CancellationToken cancellationToken = default) =>
        FindTieCollision(collisionBytes, gameId, groups, semanticLinks: false, cancellationToken);

    public static IReadOnlyList<CollisionInstancePieceCandidate> FindTieCollisionLinks(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionTieGroup> groups,
        CancellationToken cancellationToken = default) =>
        FindTieCollision(collisionBytes, gameId, groups, semanticLinks: true, cancellationToken);

    private static IReadOnlyList<CollisionInstancePieceCandidate> FindTieCollision(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionTieGroup> groups,
        bool semanticLinks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Any(group => group is null || group.TieBytes is null || group.Instances is null
            || group.Instances.Any(instance => instance is null)))
            throw new ArgumentException("TIE collision recovery groups are invalid.", nameof(groups));
        return gameId switch
        {
            GameId.UYA => (semanticLinks
                    ? UyaCollisionLinkRecovery.FindLinks(
                        collisionBytes, ToUyaTieGroups(groups), cancellationToken)
                    : UyaCollisionLinkRecovery.FindCandidates(
                        collisionBytes, ToUyaTieGroups(groups), cancellationToken))
                .Select(value => new CollisionInstancePieceCandidate(
                    value.InstanceId, value.SourcePieceIndex, value.Confidence)).ToArray(),
            _ => throw new NotSupportedException($"TIE collision link recovery does not support {gameId}."),
        };
    }

    private static UyaTieCollisionGroup[] ToUyaTieGroups(IReadOnlyList<CollisionTieGroup> groups) => groups
        .Select(group => new UyaTieCollisionGroup(
            group.TieBytes,
            group.Instances.Select(value => new UyaCollisionInstance(
                value.Id,
                UyaCollisionAdapter.ToTransform(value.Transform))).ToArray()))
        .ToArray();

    public static IReadOnlyList<CollisionInstancePieceCandidate> FindShrubCollisionCandidates(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionShrubGroup> groups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Any(group => group is null || group.ShrubBytes is null || group.Instances is null
            || group.Instances.Any(instance => instance is null)))
            throw new ArgumentException("Shrub collision recovery groups are invalid.", nameof(groups));
        return gameId switch
        {
            GameId.UYA => UyaCollisionLinkRecovery.FindShrubCandidates(
                    collisionBytes,
                    groups.Select(group => new UyaShrubCollisionGroup(
                        group.ShrubBytes,
                        group.Instances.Select(value => new UyaCollisionInstance(
                            value.Id,
                            UyaCollisionAdapter.ToTransform(value.Transform))).ToArray())).ToArray(),
                    cancellationToken)
                .Select(value => new CollisionInstancePieceCandidate(
                    value.InstanceId, value.SourcePieceIndex, value.Confidence)).ToArray(),
            _ => throw new NotSupportedException($"Shrub collision link recovery does not support {gameId}."),
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
