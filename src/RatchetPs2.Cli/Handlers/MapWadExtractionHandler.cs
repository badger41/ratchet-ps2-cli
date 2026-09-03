using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Cli.Handlers;

internal static class MapWadExtractionHandler
{
    public static MapWadExtractionResult Extract(Stream isoStream, GameId gameId, int levelId)
    {
        ArgumentNullException.ThrowIfNull(isoStream);

        return gameId switch
        {
            GameId.RC1 => Rc1MapHandler.ExtractWad(isoStream, levelId),
            GameId.GC => ExtractGc(isoStream, levelId),
            GameId.UYA => ExtractUya(isoStream, levelId),
            GameId.DL => ExtractDl(isoStream, levelId),
            _ => throw new ArgumentOutOfRangeException(nameof(gameId), gameId, "Unsupported map game.")
        };
    }

    private static MapWadExtractionResult ExtractGc(Stream isoStream, int levelId)
    {
        var levelInfo = GcLevelInfoReader.ReadLevel(isoStream, levelId);
        var wad = UyaLooseLevelWadExtractor.ExtractPrimary(
            isoStream,
            UyaMapExtractionWriter.ToUyaLevelInfo(levelInfo));
        return new(wad.Bytes, wad.SectorCount, wad.HeaderSector, wad.PayloadBaseSector);
    }

    private static MapWadExtractionResult ExtractUya(Stream isoStream, int levelId)
    {
        var wad = UyaLooseLevelWadExtractor.ExtractPrimary(isoStream, levelId);
        return new(wad.Bytes, wad.SectorCount, wad.HeaderSector, wad.PayloadBaseSector);
    }

    private static MapWadExtractionResult ExtractDl(Stream isoStream, int levelId)
    {
        var wad = DlLooseLevelWadExtractor.ExtractPrimary(isoStream, levelId);
        return new(wad.Bytes, wad.SectorCount, wad.HeaderSector, wad.PayloadBaseSector);
    }
}

internal sealed record MapWadExtractionResult(
    byte[] Bytes,
    int SectorCount,
    int HeaderSector,
    int PayloadBaseSector);
