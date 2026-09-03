using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Handlers;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExtractWadCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the game ISO.");
        var levelOption = new Option<int>("--level")
        {
            Description = "Level ID/index to extract.",
            Required = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the loose level WAD.");

        var command = CliCommandBuilder.Create(
            "extract-wad",
            "Extract a self-contained primary level WAD from a game ISO.",
            gameOption,
            inputOption,
            levelOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var level = parseResult.GetValue(levelOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (!MapGameFormats.TryParse(gameValue, out var gameId))
            {
                Console.Error.WriteLine(
                    $"Unsupported --game value '{gameValue}'. Map WAD extraction supports {MapGameFormats.SupportedGames}.");
                return 1;
            }

            if (inputFile is null)
            {
                Console.Error.WriteLine("Missing required --input option.");
                return 1;
            }

            if (outputFile is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            try
            {
                outputFile.Directory?.Create();
                using var isoStream = inputFile.OpenRead();
                var wad = MapWadExtractionHandler.Extract(isoStream, gameId, level);
                File.WriteAllBytes(outputFile.FullName, wad.Bytes);
                Console.WriteLine(
                    $"Extracted {gameId} level {level} WAD to '{outputFile.FullName}' ({wad.SectorCount} sectors, {wad.Bytes.Length} bytes, header sector 0x{wad.HeaderSector:X}, payload base 0x{wad.PayloadBaseSector:X}).");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Map WAD extraction failed: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}
