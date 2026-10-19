using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class StaticAssetComposer
{
    public static StaticAssetComposition AppendTerrainTextures(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        ReadOnlySpan<byte> paletteBytes,
        IReadOnlyList<TerrainTextureInput> terrainTextures,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaStaticAssetComposer.AppendTerrainTextures(
                headerBytes, assetWadBytes, paletteBytes, terrainTextures, cancellationToken),
            _ => throw new NotSupportedException($"Terrain texture appending is not supported for {gameId}."),
        };

    public static StaticAssetComposition Compose(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        ReadOnlySpan<byte> paletteBytes,
        IReadOnlyList<StaticAssetInput> assets,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaStaticAssetComposer.Compose(
                headerBytes, assetWadBytes, paletteBytes, assets, cancellationToken),
            _ => throw new NotSupportedException($"Static asset composition is not supported for {gameId}."),
        };

    public static StaticAssetComposition Compose(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        ReadOnlySpan<byte> paletteBytes,
        IReadOnlyList<StaticAssetInput> assets,
        IReadOnlyList<TerrainTextureInput> terrainTextures,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaStaticAssetComposer.Compose(
                headerBytes, assetWadBytes, paletteBytes, assets, terrainTextures, cancellationToken),
            _ => throw new NotSupportedException($"Static asset composition is not supported for {gameId}."),
        };
}
