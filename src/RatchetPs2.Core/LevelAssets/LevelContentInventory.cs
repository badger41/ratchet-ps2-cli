namespace RatchetPs2.Core.LevelAssets;

public sealed record LevelContentItem(string Key, int? ClassId = null, int ItemCount = 1);

public sealed record LevelContentFamily(
    string Family,
    bool Present,
    IReadOnlyList<LevelContentItem> Items,
    long EstimatedBytes,
    string? Diagnostic = null);
