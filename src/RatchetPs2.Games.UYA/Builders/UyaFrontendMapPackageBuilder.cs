using RatchetPs2.Core.Games;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

public static class UyaFrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        return BuildFiles(package.LevelWad.Level, package.Files);
    }

    public static PackedFilePackage BuildCustomMapZip(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        var package = UyaCustomMapZipUnpacker.Unpack(zipBytes);
        return BuildFiles(0, package.Files);
    }

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        return BuildPart(package.LevelWad.Level, package.Files, assetGroup);
    }

    public static PackedFilePackage BuildCustomMapZipPart(byte[] zipBytes, FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        var package = UyaCustomMapZipUnpacker.Unpack(zipBytes);
        return BuildPart(0, package.Files, assetGroup);
    }

    private static PackedFilePackage BuildFiles(int levelIndex, IReadOnlyList<PackedFile> sourceFiles)
        => BuildFiles(levelIndex, sourceFiles, FrontendMapAssetGroup.All);

    private static PackedFilePackage BuildPart(
        int levelIndex,
        IReadOnlyList<PackedFile> sourceFiles,
        FrontendMapAssetGroup assetGroup)
    {
        if (assetGroup is FrontendMapAssetGroup.All or FrontendMapAssetGroup.Common)
        {
            return assetGroup == FrontendMapAssetGroup.All
                ? BuildFiles(levelIndex, sourceFiles)
                : BuildFiles(levelIndex, sourceFiles, assetGroup);
        }

        var assets = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(sourceFiles);
        return PackedFilePackageBuilder.Pack(DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.UYA,
            levelIndex,
            assets.HeaderBytes,
            assets.PaletteBytes,
            assets.AssetWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser,
            assets.ChunkWads,
            assetGroup: assetGroup));
    }

    private static PackedFilePackage BuildFiles(
        int levelIndex,
        IReadOnlyList<PackedFile> sourceFiles,
        FrontendMapAssetGroup assetGroup)
    {
        var files = UyaLevelWadRenderPackageBuilder.BuildFiles(
            levelIndex,
            sourceFiles,
            assets => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
                GameId.UYA,
                levelIndex,
                assets.HeaderBytes,
                assets.PaletteBytes,
                assets.AssetWadBytes,
                DlLevelWadRenderPackageBuildOptions.Browser,
                assets.ChunkWads,
                assetGroup: assetGroup),
            GameId.UYA).ToList();
        var hudHeader = sourceFiles.FirstOrDefault(file => file.Path == "hud/header.bin");
        if (hudHeader is not null)
        {
            files.AddRange(HudBankRenderPackageBuilder.BuildFiles(
                hudHeader.Bytes,
                Enumerable.Range(0, HudBankReader.BankCount)
                    .Select(index => sourceFiles.FirstOrDefault(file => file.Path == $"hud/bank{index}.bin")?.Bytes ?? [])
                    .ToArray()));
        }
        return PackedFilePackageBuilder.Pack(files.Concat(sourceFiles.Where(IsGameplayMetadata)).ToArray());
    }

    private static bool IsGameplayMetadata(PackedFile file) =>
        file.Path == "gameplay/gameplay_core.bin"
        || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
}
