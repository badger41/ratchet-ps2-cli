namespace RatchetPs2.Games.DL.Level;

internal static class DlLevelCatalogReader
{
    public static IReadOnlyList<int> FindAvailable(Stream iso)
    {
        ArgumentNullException.ThrowIfNull(iso);
        var levels = new List<int>();
        for (var level = 0; level < DlLevelConstants.LevelInfoCount; level++)
            if (!DlLevelInfoReader.ReadEntry(iso, level).LevelWad.IsEmpty) levels.Add(level);
        return levels;
    }
}
