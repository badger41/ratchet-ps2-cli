using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Sdk;

public static class DlFrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
        => BuildLevelWadPart(levelWadBytes, DlLevelAssetGroup.All);

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, DlLevelAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        return PackedFilePackageBuilder.Pack(DlLevelWadRenderPackageBuilder.BuildFiles(
            levelWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser,
            assetGroup));
    }
}
