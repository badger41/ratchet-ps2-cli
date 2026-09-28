using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.LevelAssets;

public sealed record LevelAssetHeader(
    int GsRamCount,
    int GsRamOffset,
    int TerrainOffset,
    int OcclusionOffset,
    int SkyOffset,
    int CollisionOffset,
    int MobyModelCount,
    int MobyModelOffset,
    int TieModelCount,
    int TieModelOffset,
    int ShrubModelCount,
    int ShrubModelOffset,
    int TerrainTextureCount,
    int TerrainTextureOffset,
    int MobyTextureCount,
    int MobyTextureOffset,
    int TieTextureCount,
    int TieTextureOffset,
    int ShrubTextureCount,
    int ShrubTextureOffset,
    int ParticleTextureCount,
    int ParticleTextureDefOffset,
    int FxTextureCount,
    int FxTextureDefOffset,
    int TextureDataOffset,
    int ParticleTextureDataOffset,
    int FxTextureDataOffset,
    int ParticleDefOffset,
    int SoundRemapOffset,
    int DecompressionLocation,
    int LightCuboidsOffset,
    int SceneViewSize,
    int Unused2,
    int ExtraMipmapCount,
    int CompressedSize,
    int DecompressedSize,
    int ChromeTextureOffset,
    int ChromePaletteOffset,
    int GlassTextureOffset,
    int GlassPaletteOffset,
    int Unused3,
    int HeightmapOffset,
    int OcclusionOctreeOffset,
    int MobyGsStashListOffset,
    int OcclusionRadiusOffset,
    int MobySoundRemapOffset,
    int OcclusionRadius2Offset,
    int PaddingC);

public sealed record LevelAssetModelDefinition(
    int Index,
    int ModelOffset,
    int ModelId,
    int Unknown8,
    int UnknownC,
    byte[] TextureIds);

public sealed record LevelAssetShrubDefinition(
    int Index,
    int ModelOffset,
    int ModelId,
    int Unknown8,
    int UnknownC,
    byte[] TextureIds,
    short Width,
    short Height,
    short MaxMipLevel,
    short PaletteId,
    short TextureId,
    IReadOnlyList<short> Mipmaps);

public sealed record LevelAssetTextureDefinition(
    int Index,
    int TextureOffset,
    short Width,
    short Height,
    short Type,
    short PaletteId,
    short MipmapPaletteId,
    short Padding);

public sealed record LevelAssetMipmapDefinition(
    int Index,
    int TextureFormat,
    short Width,
    short Height,
    int Offset1,
    int Offset2);

public sealed record LevelParticleTextureDefinition(
    int Index,
    int PaletteOffset,
    int Unknown4,
    int TextureOffset,
    int Size);

public sealed record LevelFxTextureDefinition(
    int Index,
    int PaletteOffset,
    int TextureOffset,
    int Width,
    int Height);

public sealed record LevelAssetTexture(
    int Index,
    string Family,
    byte[] PifBytes,
    byte[] PngBytes,
    TextureAlphaInfo Alpha,
    LevelAssetTextureMetadata Metadata);

public sealed record LevelAssetTextureMetadata(
    string Family,
    int Index,
    int Width,
    int Height,
    bool IsSwizzled,
    int PaletteOffset,
    int PixelOffset,
    int PixelLength,
    IReadOnlyList<int> MipPixelOffsets,
    IReadOnlyList<int> MipPixelLengths,
    object SourceDefinition);
