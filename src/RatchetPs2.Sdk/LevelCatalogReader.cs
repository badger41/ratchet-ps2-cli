using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelCatalogReader
{
    public static IReadOnlyList<int> FindAvailable(GameId gameId, Stream iso) => gameId switch
    {
        GameId.UYA => UyaLevelCatalogReader.FindAvailable(iso),
        GameId.DL => DlLevelCatalogReader.FindAvailable(iso),
        GameId.GC => GcLevelArchiveReader.FindAvailable(iso),
        GameId.RC1 => Rc1LevelArchiveReader.FindAvailable(iso),
        _ => throw new NotSupportedException($"Level discovery is not supported for {gameId}."),
    };
}
