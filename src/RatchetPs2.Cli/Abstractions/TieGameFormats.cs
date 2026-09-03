using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;
using RatchetPs2.Games.RC1.Ties;

namespace RatchetPs2.Cli.Abstractions;

internal static class TieGameFormats
{
    public const string SupportedTieGames = "RC1, GC, UYA, or DL";

    public static TieGameProfile GetProfile(GameId gameId) => gameId == GameId.RC1
        ? Rc1TieGameProfile.Default
        : TieGameProfile.ForGame(gameId);
}
