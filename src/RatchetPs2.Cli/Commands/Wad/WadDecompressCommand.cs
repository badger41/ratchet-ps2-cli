using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Wad;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Wad;

internal static class WadDecompressCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the compressed WAD file");
        var outputOption = CommonOptions.OutputFile("Path to write the decompressed output file");

        var command = CliCommandBuilder.Create(
            "decompress",
            "Decompress a WAD-compressed file to a single output file.",
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
            var decompressedBytes = WadCompression.Decompress(stream);
            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, decompressedBytes);

            Console.WriteLine($"Decompressed WAD to '{outputFile.FullName}' ({decompressedBytes.Length} bytes).");
        });

        return command;
    }
}