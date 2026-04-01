using RatchetPs2.Cli.Abstractions;

namespace RatchetPs2.Cli.Routing;

internal sealed class CommandRouter
{
    private readonly IReadOnlyDictionary<string, ICommand> _commands;

    public CommandRouter(IEnumerable<ICommand> commands)
    {
        _commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    public int Invoke(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var commandName = args[0];

        if (IsHelpSwitch(commandName))
        {
            PrintHelp();
            return 0;
        }

        if (!_commands.TryGetValue(commandName, out var command))
        {
            Console.Error.WriteLine($"Unknown command '{commandName}'.");
            Console.Error.WriteLine();
            PrintHelp();
            return 1;
        }

        var remainingArguments = args.Skip(1).ToArray();
        return command.Invoke(remainingArguments);
    }

    private void PrintHelp()
    {
        Console.WriteLine("ratchet-ps2");
        Console.WriteLine("Cross-platform CLI for Ratchet & Clank PS2 tooling.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ratchet-ps2 <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");

        foreach (var command in _commands.Values.OrderBy(command => command.Name))
        {
            Console.WriteLine($"  {command.Name,-10} {command.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Global options:");
        Console.WriteLine("  -h|--help   Show help");
    }

    private static bool IsHelpSwitch(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}