using System.CommandLine;

namespace RatchetPs2.Cli.Abstractions;

internal static class CliCommandBuilder
{
    public static Command Create(string name, string description, params Symbol[] symbols)
    {
        var command = new Command(name, description);

        foreach (var symbol in symbols)
        {
            switch (symbol)
            {
                case Option option:
                    command.Options.Add(option);
                    break;
                case Argument argument:
                    command.Arguments.Add(argument);
                    break;
                case Command childCommand:
                    command.Subcommands.Add(childCommand);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported symbol type '{symbol.GetType().Name}'.");
            }
        }

        return command;
    }
}