using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Executables;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Cli.Handlers;

internal static class DlExecutableExportHandler
{
    public static IReadOnlyList<PackedFile> BuildFiles(Stream isoStream)
    {
        var files = new List<PackedFile> { new("boot.elf", DlExecutableReader.ReadBootElf(isoStream), "application/octet-stream") };
        for (var level = 0; level < DlLevelConstants.LevelInfoCount; level++)
        {
            if (DlLevelInfoReader.ReadEntry(isoStream, level).LevelWad.IsEmpty) continue;
            files.Add(new($"levels/{level:0000}/code/overlay.elf", DlExecutableReader.ReadOverlayElf(isoStream, level), "application/octet-stream"));
        }
        return files;
    }
}
