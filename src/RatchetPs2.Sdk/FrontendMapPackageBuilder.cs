using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;

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

    public static PackedFilePackage BuildUyaCustomMapZip(byte[] zipBytes)
        => UyaFrontendMapPackageBuilder.BuildCustomMapZip(zipBytes);
}
