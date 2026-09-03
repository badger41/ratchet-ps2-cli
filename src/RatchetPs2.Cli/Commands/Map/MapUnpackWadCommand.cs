using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Handlers;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapUnpackWadCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the raw loose level WAD.");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Directory to write unpacked files or indexed package output.",
            Required = true
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: files or indexed.",
            DefaultValueFactory = _ => "files"
        };
        var renderOption = new Option<bool>("--render")
        {
            Description = "Build the main level's render-ready package, including its mobys."
        };
        var missionsOption = new Option<bool>("--missions")
        {
            Description = "Include DL mission-specific moby models in the render package. Requires --render."
        };

        var command = CliCommandBuilder.Create(
            "unpack-wad",
            "Unpack a raw loose level WAD into files or an IndexedDB-friendly packed index.",
            gameOption,
            inputOption,
            outputOption,
            formatOption,
            renderOption,
            missionsOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputDirectory = parseResult.GetValue(outputOption);
            var format = parseResult.GetValue(formatOption);
            var render = parseResult.GetValue(renderOption);
            var missions = parseResult.GetValue(missionsOption);

            if (!MapGameFormats.TryParse(gameValue, out var gameId))
            {
                Console.Error.WriteLine(
                    $"Unsupported --game value '{gameValue}'. Map WAD unpack supports {MapGameFormats.SupportedGames}.");
                return 1;
            }

            if (inputFile is null)
            {
                Console.Error.WriteLine("Missing required --input option.");
                return 1;
            }

            if (outputDirectory is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            var normalizedFormat = (format ?? "files").Trim().ToLowerInvariant();
            if (normalizedFormat is not "files" and not "indexed")
            {
                Console.Error.WriteLine($"Unsupported --format value '{format}'. Expected files or indexed.");
                return 1;
            }
            if (missions && !render)
            {
                Console.Error.WriteLine("--missions requires --render.");
                return 1;
            }
            if (missions && gameId != GameId.DL)
            {
                Console.Error.WriteLine("--missions is currently supported only for DL render packages.");
                return 1;
            }
            try
            {
                var bytes = File.ReadAllBytes(inputFile.FullName);
                var files = MapWadHandler.BuildFiles(bytes, gameId, render, missions);
                if (normalizedFormat == "indexed")
                {
                    PackedFilePackageWriter.WriteIndexed(PackedFilePackageBuilder.Pack(files), outputDirectory);
                }
                else
                {
                    PackedFilePackageWriter.WriteFiles(files, outputDirectory);
                }

                Console.WriteLine(render
                    ? $"Built {gameId} render package from '{inputFile.FullName}' at '{outputDirectory.FullName}' ({files.Count} files)."
                    : $"Unpacked {gameId} level WAD '{inputFile.FullName}' to '{outputDirectory.FullName}' ({files.Count} files).");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"Map WAD unpack failed: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}
