using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Builders;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Builders;
using RatchetPs2.Games.RC1.Builders;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class FrontendMapPackageBuilder
{
    public static PackedFilePackage BuildLevelWad(byte[] levelWadBytes, GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return gameId switch
        {
            GameId.RC1 => Rc1FrontendMapPackageBuilder.BuildLevelWad(levelWadBytes),
            GameId.GC => GcFrontendMapPackageBuilder.BuildLevelWad(levelWadBytes),
            GameId.UYA => UyaFrontendMapPackageBuilder.BuildLevelWad(levelWadBytes),
            GameId.DL => DlFrontendMapPackageBuilder.BuildLevelWad(levelWadBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(gameId), gameId, "Unsupported map game.")
        };
    }

    public static PackedFilePackage BuildLevelWadPart(
        byte[] levelWadBytes,
        GameId gameId,
        FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return gameId switch
        {
            GameId.RC1 => Rc1FrontendMapPackageBuilder.BuildLevelWadPart(levelWadBytes, assetGroup),
            GameId.GC => GcFrontendMapPackageBuilder.BuildLevelWadPart(levelWadBytes, assetGroup),
            GameId.UYA => UyaFrontendMapPackageBuilder.BuildLevelWadPart(levelWadBytes, assetGroup),
            GameId.DL => DlFrontendMapPackageBuilder.BuildLevelWadPart(levelWadBytes, assetGroup),
            _ => throw new ArgumentOutOfRangeException(nameof(gameId), gameId, "Unsupported map game."),
        };
    }

    public static PackedFilePackage BuildCustomMapZip(byte[] zipBytes, GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        return gameId switch
        {
            GameId.UYA => UyaFrontendMapPackageBuilder.BuildCustomMapZip(zipBytes),
            _ => throw new NotSupportedException($"Custom map ZIP packages are not supported for {gameId}."),
        };
    }

    public static PackedFilePackage BuildCustomMapZipPart(
        byte[] zipBytes,
        GameId gameId,
        FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        return gameId switch
        {
            GameId.UYA => UyaFrontendMapPackageBuilder.BuildCustomMapZipPart(zipBytes, assetGroup),
            _ => throw new NotSupportedException($"Custom map ZIP packages are not supported for {gameId}."),
        };
    }

    public static PackedFilePackage BuildAssetPackage(
        GameId gameId,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetWadBytes,
        IReadOnlyDictionary<int, byte[]> chunkWads,
        FrontendMapAssetGroup assetGroup)
    {
        ArgumentNullException.ThrowIfNull(headerBytes);
        ArgumentNullException.ThrowIfNull(paletteBytes);
        ArgumentNullException.ThrowIfNull(assetWadBytes);
        ArgumentNullException.ThrowIfNull(chunkWads);
        var options = DlLevelWadRenderPackageBuildOptions.Browser with
        {
            PreserveTfragChunkNodes = assetGroup is FrontendMapAssetGroup.All
                or FrontendMapAssetGroup.Terrain,
        };
        return PackedFilePackageBuilder.Pack(DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            gameId,
            levelIndex,
            headerBytes,
            paletteBytes,
            assetWadBytes,
            options,
            chunkWads,
            assetGroup: assetGroup));
    }
}
