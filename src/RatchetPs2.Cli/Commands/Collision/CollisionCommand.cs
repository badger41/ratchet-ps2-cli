using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Collision;

internal static class CollisionCommand
{
    public static Command Build() => CliCommandBuilder.Create(
        "collision",
        "Work with level collision geometry files.",
        CollisionExportGltfCommand.Build());
}
