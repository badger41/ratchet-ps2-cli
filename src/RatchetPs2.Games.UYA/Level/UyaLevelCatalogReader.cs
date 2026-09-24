namespace RatchetPs2.Games.UYA.Level;

internal static class UyaLevelCatalogReader
{
    public static IReadOnlyList<int> FindAvailable(Stream iso)
    {
        ArgumentNullException.ThrowIfNull(iso);
        var levels = new List<int>();
        for (var level = 0; level < UyaLevelConstants.LevelInfoCount; level++)
            if (!UyaLevelInfoReader.ReadEntry(iso, level).LevelWad.IsEmpty) levels.Add(level);
        return levels;
    }
}
