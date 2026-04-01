using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.GameSelection;

internal static class GameIdParser
{
    public static bool TryParse(string value, out GameId gameId)
    {
        switch (value.Trim().ToUpperInvariant())
        {
            case "1":
            case "RC1":
                gameId = GameId.RC1;
                return true;
            case "2":
            case "GC":
                gameId = GameId.GC;
                return true;
            case "3":
            case "UYA":
                gameId = GameId.UYA;
                return true;
            case "4":
            case "DL":
                gameId = GameId.DL;
                return true;
            default:
                gameId = default;
                return false;
        }
    }
}