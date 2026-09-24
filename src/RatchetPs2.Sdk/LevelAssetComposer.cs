using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class LevelAssetComposer
{
    public static LevelAssetWadComposition ComposeAssetWad(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        LevelAssetWadPayloads replacements,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaLevelAssetComposer.ComposeAssetWad(
                headerBytes, assetWadBytes, replacements, cancellationToken),
            _ => throw new NotSupportedException($"Level asset composition is not supported for {gameId}."),
        };

    public static byte[] ComposeTfragChunk(
        GameId gameId,
        ReadOnlySpan<byte> chunkBytes,
        ReadOnlySpan<byte> terrainBytes,
        WadDecompressionOptions? decompression = null,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaLevelAssetComposer.ComposeTfragChunk(
                chunkBytes, terrainBytes, decompression, cancellationToken),
            _ => throw new NotSupportedException($"Tfrag composition is not supported for {gameId}."),
        };
}
