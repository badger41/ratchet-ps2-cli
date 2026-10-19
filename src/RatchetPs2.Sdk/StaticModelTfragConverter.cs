using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Tfrags;

namespace RatchetPs2.Sdk;

public sealed record StaticModelTfragImportResult(
    StaticModelImportResult Model,
    IReadOnlyList<StaticModelTfragPartition> Partitions);

public static class StaticModelTfragConverter
{
    public static int GetNativeTerrainGenerationVersion(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragWriter.GenerationVersion,
        _ => throw new NotSupportedException($"Static-model tfrag generation does not support {gameId}."),
    };

    public static StaticModelTextureConversionResult ConvertTextures(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragTextureConverter.Convert(asset, cancellationToken),
        _ => throw new NotSupportedException($"Static-model tfrag texture conversion does not support {gameId}."),
    };

    public static void ValidateTextureAllocation(GameId gameId, int firstTextureId, int textureCount)
    {
        switch (gameId)
        {
            case GameId.UYA:
                UyaStaticModelTfragTextureConverter.ValidateAllocation(firstTextureId, textureCount);
                break;
            default:
                throw new NotSupportedException($"Static-model tfrag texture allocation does not support {gameId}.");
        }
    }

    public static StaticModelTfragImportResult Import(
        GameId gameId,
        byte[] documentBytes,
        string sourceName,
        IReadOnlyList<StaticModelImportFile> files,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = StaticModelGltfImporter.Import(
            documentBytes,
            sourceName,
            files,
            () => cancellationToken.IsCancellationRequested);
        return new(model, Partition(gameId, model.Asset, cancellationToken));
    }

    public static IReadOnlyList<StaticModelTfragPartition> Partition(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragPartitioner.Partition(asset, cancellationToken),
        _ => throw new NotSupportedException($"Static-model tfrag partitioning does not support {gameId}."),
    };

    public static IReadOnlyList<StaticModelTfragPiece> BuildPieces(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        IReadOnlyList<StaticModelTfragPartition> partitions,
        IReadOnlyDictionary<int, int> terrainTextureIdsBySourceMaterial,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragCanonicalizer.Build(
            asset, partitions, terrainTextureIdsBySourceMaterial, cancellationToken),
        _ => throw new NotSupportedException($"Static-model tfrag construction does not support {gameId}."),
    };

    public static IReadOnlyList<StaticModelTfragPiece> BuildPieces(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        IReadOnlyList<StaticModelTfragPartition> partitions,
        StaticModelTextureConversionResult convertedTextures,
        IReadOnlyList<TerrainTextureAllocation> terrainTextures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(convertedTextures);
        ArgumentNullException.ThrowIfNull(terrainTextures);
        if (convertedTextures.GameId != gameId)
            throw new ArgumentException("Static-model texture conversion targets a different game.", nameof(convertedTextures));
        var terrainIdByAsset = terrainTextures.ToDictionary(value => value.AssetId, value => value.TextureId,
            StringComparer.Ordinal);
        var terrainIdByMaterial = convertedTextures.Materials.ToDictionary(
            value => value.SourceMaterialIndex ?? -1,
            value => terrainIdByAsset.TryGetValue(value.TextureAssetId, out var textureId)
                ? textureId
                : throw new InvalidDataException(
                    $"Converted texture {value.TextureAssetId} has no terrain texture allocation."));
        return gameId switch
        {
            GameId.UYA => UyaStaticModelTfragCanonicalizer.Build(
                asset,
                partitions,
                terrainIdByMaterial,
                cancellationToken,
                convertedTextures.Materials.ToDictionary(value => value.SourceMaterialIndex ?? -1)),
            _ => throw new NotSupportedException($"Static-model tfrag construction does not support {gameId}."),
        };
    }

    public static byte[] BuildTerrain(
        GameId gameId,
        IReadOnlyList<StaticModelTfragPiece> pieces,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragWriter.Write(pieces, cancellationToken),
        _ => throw new NotSupportedException($"Static-model tfrag native writing does not support {gameId}."),
    };

    public static int GetNativeTerrainGroupCount(
        GameId gameId,
        StaticModelTfragPiece piece,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragWriter.CountNativeGroups(piece, cancellationToken),
        _ => throw new NotSupportedException($"Static-model tfrag native writing does not support {gameId}."),
    };

}
