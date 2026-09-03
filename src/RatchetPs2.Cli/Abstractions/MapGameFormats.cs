using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Abstractions;

internal static class MapGameFormats
{
    public const string SupportedGames = "RC1, GC, UYA, or DL";

    public static bool TryParse(string? value, out GameId gameId)
    {
        gameId = default;
        return !string.IsNullOrWhiteSpace(value)
            && GameIdParser.TryParse(value, out gameId)
            && gameId is GameId.RC1 or GameId.GC or GameId.UYA or GameId.DL;
    }
}
