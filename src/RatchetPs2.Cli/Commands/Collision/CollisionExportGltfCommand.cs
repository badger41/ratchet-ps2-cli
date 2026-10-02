using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Sdk;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Collision;

internal static class CollisionExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to a collision.bin binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var minifyOption = new Option<bool>("--minify")
        {
            Description = "Write compact glTF JSON."
        };
        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export level collision geometry to a glTF model.",
            gameOption,
            inputOption,
            outputOption,
            minifyOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var minify = parseResult.GetValue(minifyOption);
            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA for collision glTF export.");
                return;
            }

            if (gameId != GameId.UYA)
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Collision glTF export currently supports only UYA. Received {gameId}.");
                return;
            }

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

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError($"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            outputFile.Directory?.Create();
            var binFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.buffer.bin");
            var export = CollisionConverter.ExportGltf(
                File.ReadAllBytes(inputFile.FullName),
                gameId,
                outputFile.Name,
                Path.GetFileName(binFile),
                minify);
            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(binFile, export.BinBytes);
            Console.WriteLine(
                $"Exported {gameId} collision glTF '{inputFile.FullName}' to '{outputFile.FullName}'.");
        });

        return command;
    }
}
