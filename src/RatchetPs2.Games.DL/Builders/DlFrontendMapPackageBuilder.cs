using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.DL.Builders;

public static class DlFrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
        => BuildLevelWadPart(levelWadBytes, FrontendMapAssetGroup.All);

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        return PackedFilePackageBuilder.Pack(DlLevelWadRenderPackageBuilder.BuildFiles(
            levelWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser,
            assetGroup));
    }
}
