using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands;

internal static class HelloCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();

        var targetArgument = new Argument<string[]>("target")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Optional words to include in the hello target."
        };

        var command = CliCommandBuilder.Create(
            "hello",
            "Print a hello-world style greeting for a selected game.",
            gameOption,
            targetArgument);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var targetWords = parseResult.GetValue(targetArgument) ?? [];

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError($"Unsupported game '{gameValue}'.");
                return;
            }

            var target = targetWords.Length > 0 ? string.Join(' ', targetWords) : "world";
            var gameModule = gameModuleResolver.Resolve(gameId);
            Console.WriteLine($"Hello, {target}! Selected game: {gameModule.Id} ({gameModule.DisplayName}).");
        });

        return command;
    }
}