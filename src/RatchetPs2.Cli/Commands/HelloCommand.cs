using RatchetPs2.Cli.Abstractions;

namespace RatchetPs2.Cli.Commands;

internal sealed class HelloCommand : ICommand
{
    public string Name => "hello";

    public string Description => "Print a hello-world style greeting.";

    public int Invoke(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0 && IsHelpSwitch(arguments[0]))
        {
            PrintHelp();
            return 0;
        }

        var target = arguments.Count > 0 ? string.Join(' ', arguments) : "world";
        Console.WriteLine($"Hello, {target}! Welcome to ratchet-ps2-cli.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ratchet-ps2 hello [target words...]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ratchet-ps2 hello");
        Console.WriteLine("  ratchet-ps2 hello Ratchet");
        Console.WriteLine("  ratchet-ps2 hello Ratchet and Clank");
    }

    private static bool IsHelpSwitch(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
}