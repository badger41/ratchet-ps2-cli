using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Sdk;

public static class LevelAssetExtractor
{
    public static LevelAssetExtractionResult ExtractLevelWad(GameId gameId, byte[] levelWadBytes) => gameId switch
    {
        GameId.UYA => UyaLevelAssetExtractor.Extract(levelWadBytes),
        _ => throw new NotSupportedException($"Level asset extraction is not supported for {gameId}."),
    };
}
