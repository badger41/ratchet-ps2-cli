using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Wad;

internal static class WadCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "wad",
            "Work with WAD-compressed files.",
            WadCompressCommand.Build(),
            WadDecompressCommand.Build());
    }
}