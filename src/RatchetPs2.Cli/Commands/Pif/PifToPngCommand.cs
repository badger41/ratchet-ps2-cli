using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Pif;

internal static class PifToPngCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the input PIF texture file");
        var outputOption = CommonOptions.OutputFile("Path to write the output PNG file");
        var doubleAlphaOption = new Option<bool>("--double-alpha")
        {
            Description = "Double alpha values while converting, useful for some UI/minimap textures."
        };
        var pngFormatOption = new Option<string>("--png-format")
        {
            Description = "PNG output format: rgba32, indexed8, or indexed4. Defaults to rgba32.",
            DefaultValueFactory = _ => "rgba32"
        };

        var command = CliCommandBuilder.Create(
            "to-png",
            "Convert a PIF texture file to a PNG image.",
            inputOption,
            outputOption,
            doubleAlphaOption,
            pngFormatOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var doubleAlpha = parseResult.GetValue(doubleAlphaOption);
            var pngFormatValue = parseResult.GetValue(pngFormatOption);

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

            if (!TryParseTexturePixelFormat(pngFormatValue, out var pngPixelFormat))
            {
                parseResult.GetResult(pngFormatOption)?.AddError(
                    $"Unsupported --png-format value '{pngFormatValue}'. Expected rgba32, indexed8, or indexed4.");
                return;
            }

            using var stream = inputFile.OpenRead();
            var texture = PifReader.Read(stream);
            var pngBytes = TextureConverter.ConvertToPng(
                texture,
                pngPixelFormat,
                new TextureConversionOptions
                {
                    DoubleAlpha = doubleAlpha,
                });

            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, pngBytes);

            Console.WriteLine(
                $"Converted '{inputFile.FullName}' to '{outputFile.FullName}' as {texture.Header.USize}x{texture.Header.VSize} PNG.");
        });

        return command;
    }

    private static bool TryParseTexturePixelFormat(string? value, out TexturePixelFormat pngPixelFormat)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "rgba32":
                pngPixelFormat = TexturePixelFormat.Rgba32;
                return true;
            case "indexed8":
                pngPixelFormat = TexturePixelFormat.Indexed8;
                return true;
            case "indexed4":
                pngPixelFormat = TexturePixelFormat.Indexed4;
                return true;
            default:
                pngPixelFormat = default;
                return false;
        }
    }
}