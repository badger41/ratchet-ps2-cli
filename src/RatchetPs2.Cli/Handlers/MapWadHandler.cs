using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Cli.Handlers;

internal static class MapWadHandler
{
    public static IReadOnlyList<PackedFile> BuildFiles(
        byte[] levelWadBytes,
        GameId gameId,
        bool render,
        bool includeMissions)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return gameId switch
        {
            GameId.RC1 => Rc1MapHandler.BuildFiles(levelWadBytes, render),
            GameId.GC or GameId.UYA => BuildUyaFiles(levelWadBytes, gameId, render),
            GameId.DL => BuildDlFiles(levelWadBytes, render, includeMissions),
            _ => throw new ArgumentOutOfRangeException(nameof(gameId), gameId, "Unsupported map game.")
        };
    }

    private static IReadOnlyList<PackedFile> BuildUyaFiles(
        byte[] levelWadBytes,
        GameId gameId,
        bool render)
    {
        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        if (!render)
        {
            return package.Files;
        }

        return UyaLevelWadRenderPackageBuilder.BuildFiles(
            package.LevelWad.Level,
            package.Files,
            assets => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
                gameId,
                package.LevelWad.Level,
                assets.HeaderBytes,
                assets.PaletteBytes,
                assets.AssetWadBytes,
                BrowserOptions,
                assets.ChunkWads,
                gameId == GameId.GC
                    ? GcSkyRotationReader.ReadRadiansPerFrame(assets.CodeBytes)
                    : null),
            gameId);
    }

    private static IReadOnlyList<PackedFile> BuildDlFiles(
        byte[] levelWadBytes,
        bool render,
        bool includeMissions)
    {
        return render
            ? DlLevelWadRenderPackageBuilder.BuildFiles(
                levelWadBytes,
                BrowserOptions with { IncludeMissionMobys = includeMissions })
            : DlLevelWadUnpacker.Unpack(levelWadBytes).Files;
    }

    private static DlLevelWadRenderPackageBuildOptions BrowserOptions =>
        DlLevelWadRenderPackageBuildOptions.Browser with
        {
            IncludeDiagnostics = true,
            MobyLodIndex = null
        };
}
