using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.GC.Level;

internal static class GcLevelArchiveReader
{
    public static IReadOnlyList<int> FindAvailable(Stream iso)
    {
        ArgumentNullException.ThrowIfNull(iso);
        var levels = new List<int>();
        foreach (var level in GcLevelCatalog.Levels)
        {
            var block = GcLevelInfoReader.ReadLevel(iso, level.Id).LevelWad;
            if (block.Offset < 0 || block.Length < 0)
                throw new InvalidDataException("GC level WAD contains negative sector values.");
            if (block.Offset != 0 && block.Length != 0) levels.Add(level.Id);
        }
        return levels;
    }

    public static byte[] ExtractPrimary(Stream iso, int level)
    {
        var entry = GcLevelInfoReader.ReadLevel(iso, level);
        return UyaLooseLevelWadExtractor.ExtractPrimary(iso, new UyaLevelInfoSet(
            entry.Level.TableIndex, new UyaLevelInfoEntry(entry.Level.TableIndex,
                new(entry.LevelAudioWad.Offset, entry.LevelAudioWad.Length),
                new(entry.LevelWad.Offset, entry.LevelWad.Length),
                new(entry.LevelSceneWad.Offset, entry.LevelSceneWad.Length)))).Bytes;
    }
}
