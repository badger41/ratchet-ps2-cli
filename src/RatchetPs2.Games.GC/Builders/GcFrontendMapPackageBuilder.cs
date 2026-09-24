using RatchetPs2.Core.Games;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.GC.Builders;

public static class GcFrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes)
        => BuildLevelWadPart(levelWadBytes, FrontendMapAssetGroup.All);

    public static PackedFilePackage BuildLevelWadPart(byte[] levelWadBytes, FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        if (assetGroup is not (FrontendMapAssetGroup.All or FrontendMapAssetGroup.Common))
        {
            return PackedFilePackageBuilder.Pack(BuildAssetFiles(
                package.LevelWad.Level,
                UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(package.Files),
                assetGroup));
        }
        var files = UyaLevelWadRenderPackageBuilder.BuildFiles(
            package.LevelWad.Level,
            package.Files,
            source => BuildAssetFiles(package.LevelWad.Level, source, assetGroup),
            GameId.GC).ToList();
        var hudHeader = package.Files.FirstOrDefault(file => file.Path == "hud/header.bin");
        if (hudHeader is not null)
        {
            files.AddRange(HudBankRenderPackageBuilder.BuildFiles(
                hudHeader.Bytes,
                Enumerable.Range(0, HudBankReader.BankCount)
                    .Select(index => package.Files.FirstOrDefault(file => file.Path == $"hud/bank{index}.bin")?.Bytes ?? [])
                    .ToArray()));
        }
        return PackedFilePackageBuilder.Pack(files.Concat(package.Files.Where(IsGameplayMetadata)).ToArray());
    }

    private static IReadOnlyList<PackedFile> BuildAssetFiles(
        int levelIndex,
        UyaLevelAssetSourceFiles assets,
        FrontendMapAssetGroup assetGroup) =>
        DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.GC,
            levelIndex,
            assets.HeaderBytes,
            assets.PaletteBytes,
            assets.AssetWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser,
            assets.ChunkWads,
            GcSkyRotationReader.ReadRadiansPerFrame(assets.CodeBytes),
            assetGroup);

    private static bool IsGameplayMetadata(PackedFile file) =>
        file.Path == "gameplay/gameplay_core.bin"
        || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
}
