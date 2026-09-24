using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Sdk;

public static class LevelArchiveReader
{
    public static byte[] ExtractPrimary(GameId gameId, Stream iso, int levelIndex) => gameId switch
    {
        GameId.UYA => UyaLooseLevelWadExtractor.ExtractPrimary(iso, levelIndex).Bytes,
        _ => throw new NotSupportedException($"Level archive extraction is not supported for {gameId}."),
    };
}
