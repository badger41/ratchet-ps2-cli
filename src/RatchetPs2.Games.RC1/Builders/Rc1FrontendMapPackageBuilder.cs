using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Games.RC1.Builders;

public static class Rc1FrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
        => BuildLevelWadPart(levelWadBytes, FrontendMapAssetGroup.All);

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        var package = Rc1LevelWadUnpacker.Unpack(levelWadBytes);
        if (assetGroup is not (FrontendMapAssetGroup.All or FrontendMapAssetGroup.Common))
        {
            return PackedFilePackageBuilder.Pack(BuildAssetFiles(
                package.LevelWad.Level,
                Rc1LevelWadRenderPackageBuilder.GetAssetSources(package.Files),
                assetGroup));
        }
        var files = Rc1LevelWadRenderPackageBuilder.BuildFiles(
            package.LevelWad.Level,
            package.Files,
            assets => BuildAssetFiles(package.LevelWad.Level, assets, assetGroup));
        return PackedFilePackageBuilder.Pack(files.Concat(package.Files.Where(IsGameplayMetadata)).ToArray());
    }

    private static IReadOnlyList<PackedFile> BuildAssetFiles(
        int levelIndex,
        Rc1LevelAssetSourceFiles assets,
        FrontendMapAssetGroup assetGroup) =>
        DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.RC1,
            levelIndex,
            assets.HeaderBytes,
            assets.PaletteBytes,
            assets.AssetWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser with
            {
                AssetProfile = Rc1LevelAssetProfile.Default
            },
            assetGroup: assetGroup);

    private static bool IsGameplayMetadata(PackedFile file) =>
        file.Path == "gameplay/gameplay_core.bin"
        || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
}
