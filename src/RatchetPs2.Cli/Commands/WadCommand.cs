using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Cli.Commands;

internal sealed class WadCommand : ICommand
{
    public string Name => "wad";

    public string Description => "Decompress WAD-compressed files.";

    public int Invoke(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || IsHelpSwitch(arguments[0]))
        {
            PrintHelp();
            return 0;
        }

        if (!arguments[0].Equals("decompress", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown wad subcommand '{arguments[0]}'.");
            Console.Error.WriteLine();
            PrintHelp();
            return 1;
        }

        if (!TryParseDecompressArguments(arguments.Skip(1).ToArray(), out var inputPath, out var outputPath, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            PrintDecompressHelp();
            return 1;
        }

        try
        {
            using var stream = File.OpenRead(inputPath);

            var decompressedBytes = WadCompression.Decompress(stream);
            var outputParentDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputParentDirectory))
            {
                Directory.CreateDirectory(outputParentDirectory);
            }

            File.WriteAllBytes(outputPath, decompressedBytes);

            Console.WriteLine($"Decompressed WAD to '{outputPath}' ({decompressedBytes.Length} bytes).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to decompress WAD: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseDecompressArguments(
        IReadOnlyList<string> arguments,
        out string inputPath,
        out string outputPath,
        out string errorMessage)
    {
        inputPath = string.Empty;
        outputPath = string.Empty;
        errorMessage = string.Empty;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument.Equals("--input", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    errorMessage = "The --input option requires a file path.";
                    return false;
                }

                inputPath = arguments[++index];
                continue;
            }

            if (argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    errorMessage = "The --output option requires a directory path.";
                    return false;
                }

                outputPath = arguments[++index];
                continue;
            }

            if (IsHelpSwitch(argument))
            {
                errorMessage = string.Empty;
                return false;
            }

            errorMessage = $"Unknown option '{argument}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            errorMessage = "Missing required --input option.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            errorMessage = "Missing required --output option.";
            return false;
        }

        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ratchet-ps2 wad decompress --input <file.wad> --output <file.bin>");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  decompress Decompress a WAD-compressed file to a single output file.");
    }

    private static void PrintDecompressHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ratchet-ps2 wad decompress --input <file.wad> --output <file.bin>");
        Console.WriteLine();
        Console.WriteLine("Behavior:");
        Console.WriteLine("  - validates that the input begins with WAD magic");
        Console.WriteLine("  - decompresses the file using the game's WAD compression");
        Console.WriteLine("  - writes the raw decompressed bytes to the output path");
    }

    private static bool IsHelpSwitch(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}