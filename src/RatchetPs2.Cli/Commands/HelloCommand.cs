using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Commands;

internal sealed class HelloCommand : ICommand
{
    private readonly GameModuleResolver _gameModuleResolver;

    public HelloCommand(GameModuleResolver gameModuleResolver)
    {
        _gameModuleResolver = gameModuleResolver;
    }

    public string Name => "hello";

    public string Description => "Print a hello-world style greeting for a selected game.";

    public int Invoke(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0 && IsHelpSwitch(arguments[0]))
        {
            PrintHelp();
            return 0;
        }

        if (!TryParseArguments(arguments, out var gameId, out var target, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            PrintHelp();
            return 1;
        }

        var gameModule = _gameModuleResolver.Resolve(gameId);
        Console.WriteLine($"Hello, {target}! Selected game: {gameModule.Id} ({gameModule.DisplayName}).");
        return 0;
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> arguments,
        out GameId gameId,
        out string target,
        out string errorMessage)
    {
        gameId = default;
        target = "world";
        errorMessage = string.Empty;

        if (arguments.Count == 0)
        {
            errorMessage = "Missing required --game option.";
            return false;
        }

        var remainingWords = new List<string>();

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument.Equals("--game", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    errorMessage = "The --game option requires a value (1-4, RC1, GC, UYA, or DL).";
                    return false;
                }

                if (!GameIdParser.TryParse(arguments[index + 1], out gameId))
                {
                    errorMessage = $"Unsupported game '{arguments[index + 1]}'.";
                    return false;
                }

                index++;
                continue;
            }

            remainingWords.Add(argument);
        }

        if (gameId == default)
        {
            errorMessage = "Missing required --game option.";
            return false;
        }

        if (remainingWords.Count > 0)
        {
            target = string.Join(' ', remainingWords);
        }

        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ratchet-ps2 hello --game <1|2|3|4|RC1|GC|UYA|DL> [target words...]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ratchet-ps2 hello --game 1");
        Console.WriteLine("  ratchet-ps2 hello --game GC Ratchet");
        Console.WriteLine("  ratchet-ps2 hello --game 3 Ratchet and Clank");
    }

    private static bool IsHelpSwitch(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
}