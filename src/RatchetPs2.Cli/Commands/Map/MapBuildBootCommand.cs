using System.CommandLine;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Handlers;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapBuildBootCommand
{
    public static Command Build()
    {
        var configOption = new Option<FileInfo>("--config")
        {
            Description = "config.ini containing source_iso, boot_elf, and output_iso paths.",
            Required = true
        };
        var command = CliCommandBuilder.Create("build-boot",
            "Build a DL ISO using the current boot ELF selected by config.ini.", configOption);
        command.SetAction(result =>
        {
            var config = result.GetValue(configOption);
            if (config is null) return 1;
            try
            {
                var settings = BootBuildConfiguration.Read(config.FullName);
                if (string.Equals(settings.OutputIso, config.FullName,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("Output ISO cannot overwrite config.ini.");
                DlBootBuildHandler.Build(settings);
                Console.WriteLine($"Built '{settings.OutputIso}' with byte-verified boot ELF '{settings.BootElf}'.");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException)
            {
                Console.Error.WriteLine($"Boot ISO build failed: {ex.Message}");
                return 1;
            }
        });
        return command;
    }
}
