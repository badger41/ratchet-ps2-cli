using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class Rc1FrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
        => BuildLevelWadPart(levelWadBytes, DlLevelAssetGroup.All);

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, DlLevelAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        var package = Rc1LevelWadUnpacker.Unpack(levelWadBytes);
        if (assetGroup is not (DlLevelAssetGroup.All or DlLevelAssetGroup.Common))
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
        return FrontendMapPackage.PackWithGameplay(files, package.Files);
    }

    private static IReadOnlyList<PackedFile> BuildAssetFiles(
        int levelIndex,
        Rc1LevelAssetSourceFiles assets,
        DlLevelAssetGroup assetGroup) =>
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
}
