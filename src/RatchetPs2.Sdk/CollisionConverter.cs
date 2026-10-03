using RatchetPs2.Core.Games;

namespace RatchetPs2.Sdk;

public static class CollisionConverter
{
    public static CollisionInspection Inspect(byte[] collisionBytes, GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.Inspect(collisionBytes),
            _ => throw new NotSupportedException($"Map collision inspection does not support {gameId}."),
        };
    }

    public static CollisionAnalysis Analyze(byte[] collisionBytes, GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        return gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.Analyze(collisionBytes),
            _ => throw new NotSupportedException($"Map collision analysis does not support {gameId}."),
        };
    }

    public static CollisionAnalysis AnalyzeComposition(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits,
        IReadOnlyList<CollisionSolidAddition> additions)
    {
        return CollisionWork.AnalyzeComposition(collisionBytes, gameId, edits, additions);
    }

    public static TieCollisionCandidate GenerateTieSurfaceCandidate(
        byte[] tieBytes,
        GameId gameId,
        string additionId,
        int lodIndex = 0,
        byte rawType = 0,
        int maximumFaces = CollisionWork.DefaultMaximumFaces) => CollisionWork.GenerateTieSurfaceCandidate(
            tieBytes, gameId, additionId, lodIndex, rawType, maximumFaces);

    public static TieCollisionCandidate GenerateTieDecimatedCandidate(
        byte[] tieBytes,
        GameId gameId,
        string additionId,
        byte rawType = 0,
        int maximumFaces = CollisionWork.DefaultMaximumFaces) => CollisionWork.GenerateTieDecimatedCandidate(
            tieBytes, gameId, additionId, rawType, maximumFaces);

    public static CollisionComposition Compose(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits)
        => Compose(collisionBytes, gameId, edits, []);

    public static CollisionComposition Compose(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits,
        IReadOnlyList<CollisionSolidAddition> additions)
    {
        return CollisionWork.Compose(collisionBytes, gameId, edits, additions);
    }

    public static CollisionComposition EncodeStandalone(
        GameId gameId,
        IReadOnlyList<CollisionSolidAddition> additions) => CollisionWork.EncodeStandalone(gameId, additions);

    public static CollisionSolidAddition TransformAddition(
        CollisionSolidAddition addition,
        GameId gameId,
        string additionId,
        CollisionInstanceTransform transform) => CollisionWork.TransformAddition(
            addition, gameId, additionId, transform);

    public static CollisionSolidAddition DecodeSolidAddition(
        byte[] collisionBytes,
        GameId gameId,
        string additionId) => CollisionWork.DecodeSolidAddition(collisionBytes, gameId, additionId);

    public static CollisionGltfFiles ExportGltf(
        byte[] collisionBytes,
        GameId gameId,
        string gltfFileName = "collision.gltf",
        string? bufferFileName = null,
        bool minify = false,
        CollisionGltfPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        var export = gameId switch
        {
            GameId.UYA => UyaCollisionAdapter.ExportGltf(
                collisionBytes, gltfFileName, bufferFileName, minify, palette),
            _ => throw new NotSupportedException($"Map collision glTF export does not support {gameId}.")
        };
        return new(export.GltfBytes, export.BinBytes);
    }
}
