using RatchetPs2.Core.Sounds;

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
    IReadOnlyList<ExtractedLevelAssetTexture> Textures,
    string? SourceArchivePath = null);

public sealed record LevelAssetExtractionResult(
    IReadOnlyList<ExtractedLevelAsset> Assets,
    int FailedAssetCount)
{
    public byte[]? SkyboxBytes { get; init; }
    public IReadOnlyList<ExtractedLevelSoundBank> SoundBanks { get; init; } = [];
}

public sealed record ExtractedLevelMobySounds(int SourceIndex, int ClassId, IReadOnlyList<int> Programs, string ModelArchivePath);

public sealed record ExtractedLevelSoundBank(byte[] Bytes, string ArchivePath,
    SoundBankInventory Inventory, IReadOnlyList<ExtractedLevelMobySounds> Mobys);
