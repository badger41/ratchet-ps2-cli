using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Pif;

internal static class PifCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "pif",
            "Work with PIF texture files.",
            PifToPngCommand.Build());
    }
}