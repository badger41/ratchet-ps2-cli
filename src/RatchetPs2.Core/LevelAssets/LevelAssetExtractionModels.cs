namespace RatchetPs2.Core.LevelAssets;

public sealed record ExtractedLevelAssetTexture(
    byte Role,
    byte[]? PifBytes,
    string? Error = null);

public sealed record ExtractedLevelAsset(
    FrontendAssetKind Kind,
    int ClassId,
    int SourceIndex,
    byte[] DefinitionBytes,
    byte[] ModelBytes,
    IReadOnlyList<ExtractedLevelAssetTexture> Textures);

public sealed record LevelAssetExtractionResult(
    IReadOnlyList<ExtractedLevelAsset> Assets,
    int FailedAssetCount);
