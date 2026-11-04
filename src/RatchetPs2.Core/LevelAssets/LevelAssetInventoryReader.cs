using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Core.LevelAssets;

public static class LevelAssetInventoryReader
{
    public static LevelAssetExtractionResult Extract(byte[] headerBytes, byte[] paletteBytes, byte[] assetWadBytes)
    {
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var assetBytes = BinaryMagic.IsWad(assetWadBytes)
            ? WadCompression.Decompress(assetWadBytes)
            : assetWadBytes;
        var mobys = LevelAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var knownOffsets = LevelAssetReader.CollectKnownAssetOffsets(
            header, assetBytes.Length, mobys, ties, shrubs, [header.SceneViewSize]);
        var mipmaps = LevelAssetReader.ReadMipmapDefinitions(
            headerBytes, header.GsRamOffset, Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStash = mipmaps.Skip(header.GsRamCount).ToArray();
        var mobyTextures = LevelAssetReader.ReadTextureDefinitions(
            headerBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextures = LevelAssetReader.ReadTextureDefinitions(
            headerBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextures = LevelAssetReader.ReadTextureDefinitions(
            headerBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var assets = new List<ExtractedLevelAsset>(mobys.Count + ties.Count + shrubs.Count);
        var failures = 0;

        foreach (var definition in mobys)
        {
            try
            {
                Add(assets, FrontendAssetKind.Moby, definition.ModelId, definition.Index,
                    headerBytes.AsSpan(header.MobyModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    LevelAssetTextureExtractor.Extract("moby", definition.TextureIds, mobyTextures, paletteBytes, assetBytes,
                        header.TextureDataOffset, gsStash, isSwizzled: false));
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                failures++;
            }
        }
        foreach (var definition in ties)
        {
            try
            {
                Add(assets, FrontendAssetKind.Tie, definition.ModelId, definition.Index,
                    headerBytes.AsSpan(header.TieModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    LevelAssetTextureExtractor.Extract("tie", definition.TextureIds, tieTextures, paletteBytes, assetBytes,
                        header.TextureDataOffset, null, isSwizzled: false));
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                failures++;
            }
        }
        foreach (var definition in shrubs)
        {
            try
            {
                var textures = LevelAssetTextureExtractor.Extract("shrub", definition.TextureIds, shrubTextures, paletteBytes,
                    assetBytes, header.TextureDataOffset, null, isSwizzled: false).ToList();
                if (definition.Width > 0 && definition.Height > 0 && definition.TextureId > 0)
                    textures.Add(new(1, LevelAssetReader.BuildShrubBillboardTexture(definition, paletteBytes).PifBytes));
                Add(assets, FrontendAssetKind.Shrub, definition.ModelId, definition.Index,
                    headerBytes.AsSpan(header.ShrubModelOffset + definition.Index * 0x30, 0x30).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets), textures);
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                failures++;
            }
        }
        var sky = LevelAssetReader.ReadAssetSlice(assetBytes, header.SkyOffset, knownOffsets);
        return new(assets, failures)
        {
            SkyboxBytes = sky.Length == 0 ? null : sky,
        };
    }

    private static void Add(
        ICollection<ExtractedLevelAsset> assets,
        FrontendAssetKind kind,
        int classId,
        int sourceIndex,
        byte[] definitionBytes,
        byte[] modelBytes,
        IReadOnlyList<ExtractedLevelAssetTexture> textures)
    {
        assets.Add(new(kind, classId, sourceIndex, definitionBytes, modelBytes, textures));
    }

    private static bool IsUnsupportedAsset(Exception exception) => exception is
        ArgumentException or InvalidDataException or IOException or NotSupportedException or OverflowException;
}
