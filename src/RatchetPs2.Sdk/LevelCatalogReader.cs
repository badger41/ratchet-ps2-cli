using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Sdk;

public static class LevelCatalogReader
{
    public static IReadOnlyList<int> FindAvailable(GameId gameId, Stream iso) => gameId switch
    {
        GameId.UYA => UyaLevelCatalogReader.FindAvailable(iso),
        GameId.DL => DlLevelCatalogReader.FindAvailable(iso),
        _ => throw new NotSupportedException($"Level discovery is not supported for {gameId}."),
    };
}
