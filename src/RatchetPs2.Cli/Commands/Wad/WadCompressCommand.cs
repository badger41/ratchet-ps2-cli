using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Wad;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Wad;

internal static class WadCompressCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the decompressed input file");
        var outputOption = CommonOptions.OutputFile("Path to write the compressed WAD file");

        var command = CliCommandBuilder.Create(
            "compress",
            "Compress a file using the game's WAD compression.",
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            using var stream = inputFile.OpenRead();
            var compressedBytes = WadCompression.Compress(stream);
            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, compressedBytes);

            Console.WriteLine($"Compressed WAD to '{outputFile.FullName}' ({compressedBytes.Length} bytes).");
        });

        return command;
    }
}