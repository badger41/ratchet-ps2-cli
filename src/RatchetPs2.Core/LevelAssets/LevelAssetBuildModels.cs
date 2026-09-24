using RatchetPs2.Core.Textures.Palettes;

namespace RatchetPs2.Core.LevelAssets;

public enum FrontendAssetKind
{
    Moby = 1,
    Tie = 2,
    Shrub = 3,
}

public sealed record FrontendAssetTexture(byte Role, byte[] PifBytes);

public sealed record LevelAssetWadPayloads(
    ReadOnlyMemory<byte>? Terrain = null,
    ReadOnlyMemory<byte>? Sky = null,
    ReadOnlyMemory<byte>? Collision = null);

public sealed record LevelAssetWadComposition(
    byte[] HeaderBytes,
    byte[] AssetWadBytes);

public sealed record StaticAssetTexture(
    TextureRole Role,
    ReadOnlyMemory<byte> PifBytes);

public sealed record StaticAssetInput(
    string AssetId,
    TextureAssetFamily Family,
    int ClassId,
    ReadOnlyMemory<byte> DefinitionBytes,
    ReadOnlyMemory<byte> ModelBytes,
    IReadOnlyList<StaticAssetTexture> Textures);

public sealed record StaticAssetComposition(
    byte[] HeaderBytes,
    byte[] AssetWadBytes,
    byte[] PaletteBytes,
    TextureInventory Inventory,
    PaletteOptimizationResult Optimization);
