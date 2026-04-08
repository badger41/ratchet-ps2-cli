using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Wad;

internal static class WadUnpackTocCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the TOC-backed data file");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Path to the output directory for extracted entries.",
            Required = true
        };
        var offsetOption = new Option<string>("--offset")
        {
            Description = "Optional TOC start offset in decimal or hex (for example 4096 or 0x1000).",
            DefaultValueFactory = _ => "0"
        };

        var command = CliCommandBuilder.Create(
            "unpack-toc",
            "Extract entries from a TOC-backed data block, writing WAD entries as .wad and all others as .bin.",
            inputOption,
            outputOption,
            offsetOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputDirectory = parseResult.GetValue(outputOption);
            var offsetValue = parseResult.GetValue(offsetOption);

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputDirectory is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            if (!TryParseOffset(offsetValue, out var startOffset))
            {
                parseResult.GetResult(offsetOption)?.AddError(
                    $"Unsupported --offset value '{offsetValue}'. Expected a decimal number or a hex value like 0x1000.");
                return;
            }

            using var stream = inputFile.OpenRead();
            var archive = WadTocReader.Read(stream, startOffset);

            outputDirectory.Create();

            foreach (var entry in archive.Entries)
            {
                var bytes = WadTocReader.ReadEntryBytes(stream, archive, entry);
                WriteProcessedOutput(outputDirectory.FullName, $"{entry.Index:D4}", bytes);
            }

            Console.WriteLine(
                $"Extracted {archive.Entries.Count} TOC entries from '{inputFile.FullName}' to '{outputDirectory.FullName}'.");
        });

        return command;
    }

    private static bool TryParseOffset(string? value, out long offset)
    {
        offset = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out offset);
        }

        return long.TryParse(trimmed, out offset);
    }

    private static void WriteProcessedOutput(string outputDirectory, string baseName, byte[] bytes)
    {
        switch (BinaryMagic.Detect(bytes))
        {
            case BinaryDataKind.Pif:
                WritePif(outputDirectory, baseName, bytes);
                return;
            case BinaryDataKind.Wad:
                WriteDecompressedWad(outputDirectory, baseName, bytes);
                return;
            default:
                File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.bin"), TrimTrailingZeros(bytes));
                return;
        }
    }

    private static void WritePif(string outputDirectory, string baseName, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var exported = PifAssetExporter.Export(stream, TexturePixelFormat.Rgba32);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.pif"), exported.PifBytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.png"), exported.PngBytes);
    }

    private static void WriteDecompressedWad(string outputDirectory, string baseName, byte[] bytes)
    {
        try
        {
            var decompressedBytes = WadCompression.Decompress(bytes);
            WriteProcessedOutput(outputDirectory, baseName, decompressedBytes);
        }
        catch (InvalidDataException)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.wad"), TrimTrailingZeros(bytes));
        }
    }

    private static byte[] TrimTrailingZeros(byte[] bytes)
    {
        var trimmedLength = bytes.Length;

        while (trimmedLength > 0 && bytes[trimmedLength - 1] == 0)
        {
            trimmedLength--;
        }

        return trimmedLength == bytes.Length
            ? bytes
            : bytes[..trimmedLength];
    }
}