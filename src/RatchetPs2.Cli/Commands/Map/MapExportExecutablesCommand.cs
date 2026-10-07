using System.CommandLine;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Handlers;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExportExecutablesCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the retail Deadlocked ISO.");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Empty directory for boot.elf and levels/<id>/code/overlay.elf.",
            Required = true
        };
        var bootElfOption = new Option<FileInfo>("--boot-elf")
        {
            Description = "Compiler output to reference in config.ini; defaults to the exported boot.elf."
        };
        var outputIsoOption = new Option<FileInfo>("--output-iso")
        {
            Description = "ISO build destination stored in config.ini; defaults to new_dl.iso in the export directory."
        };
        var command = CliCommandBuilder.Create("export-executables",
            "Reconstruct DL boot and level overlay ELFs and create an ISO build configuration.",
            gameOption, inputOption, outputOption, bootElfOption, outputIsoOption);
        command.SetAction(result =>
        {
            if (!MapGameFormats.TryParse(result.GetValue(gameOption), out var game) || game != GameId.DL)
            {
                Console.Error.WriteLine("Executable export currently supports --game DL only.");
                return 1;
            }
            var input = result.GetValue(inputOption);
            var output = result.GetValue(outputOption);
            if (input is null || output is null) return 1;
            try
            {
                if (output.Exists && output.EnumerateFileSystemInfos().Any(entry =>
                    entry is not FileInfo { Name: ".gitkeep", Length: 0 }))
                    throw new IOException("Output directory must be empty; existing assets will not be overwritten.");
                using var source = input.OpenRead();
                var files = DlExecutableExportHandler.BuildFiles(source);
                PackedFilePackageWriter.WriteFiles(files, output);
                BootBuildConfiguration.Write(Path.Combine(output.FullName, "config.ini"), input.FullName,
                    result.GetValue(bootElfOption)?.FullName ?? Path.Combine(output.FullName, "boot.elf"),
                    result.GetValue(outputIsoOption)?.FullName ?? Path.Combine(output.FullName, "new_dl.iso"));
                Console.WriteLine($"Exported {files.Count} ELFs and config.ini to '{output.FullName}'. Use map build-boot --config to build an ISO.");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException)
            {
                Console.Error.WriteLine($"Executable export failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }
}
