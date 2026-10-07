using RatchetPs2.Core.Disc;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.DL.Executables;

public static class DlExecutableReader
{
    public static byte[] ReadBootElf(Stream isoStream)
    {
        ValidateProfile(isoStream);
        return DlElfBuilder.BuildBoot(Iso9660RootReader.ReadFile(isoStream, "SCUS_974.65"));
    }

    private static void ValidateProfile(Stream isoStream)
    {
        var metadata = PlayStation2DiscReader.Read(isoStream);
        // The existing DL level-table reader currently describes the retail layout.
        if (metadata.Serial != "SCUS-97465" || metadata.Revision != "1.00")
            throw new NotSupportedException("Executable export currently supports Deadlocked SCUS-97465 revision 1.00.");
    }

    public static byte[] ReadOverlayElf(Stream isoStream, int levelIndex)
    {
        ValidateProfile(isoStream);
        var info = DlLevelInfoReader.ReadEntry(isoStream, levelIndex);
        if (info.LevelWad.IsEmpty) throw new InvalidDataException($"Level {levelIndex} has no WAD.");
        var header = DlLevelWadReader.ReadLevelWad(DlLevelInfoReader.ReadSectorHeader(
            isoStream, info.LevelWad, DlLevelConstants.LevelWadHeaderSectorCount));
        var core = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, header.Sector, header.Data);
        var code = DlCoreLevelSegmentReader.Read(core).SingleOrDefault(segment => segment.HeaderOffset == 8)
            ?? throw new InvalidDataException($"Level {levelIndex} has no executable segment.");
        return DlElfBuilder.BuildOverlay(code.PayloadBytes);
    }
}
