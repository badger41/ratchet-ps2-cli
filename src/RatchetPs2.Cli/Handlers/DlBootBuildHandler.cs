using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Games.DL.Builders;

namespace RatchetPs2.Cli.Handlers;

internal static class DlBootBuildHandler
{
    public static void Build(BootBuildConfiguration configuration)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(configuration.SourceIso, configuration.OutputIso, comparison) ||
            string.Equals(configuration.BootElf, configuration.OutputIso, comparison))
            throw new IOException("Output ISO must differ from both input paths.");
        // Snapshot the current compiler output before starting the disc copy.
        var boot = File.ReadAllBytes(configuration.BootElf);
        DlBootIsoBuilder.ValidateElf(boot);
        using var source = File.OpenRead(configuration.SourceIso);
        var directory = Path.GetDirectoryName(configuration.OutputIso)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".ratchet-ps2-{Guid.NewGuid():N}.iso.tmp");
        try
        {
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                DlBootIsoBuilder.Build(source, destination, boot);
            // Only replace the previous build after the new image verifies successfully.
            File.Move(temporary, configuration.OutputIso, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
