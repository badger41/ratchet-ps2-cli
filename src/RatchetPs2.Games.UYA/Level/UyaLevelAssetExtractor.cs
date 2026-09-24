using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.UYA.Level;

internal static class UyaLevelAssetExtractor
{
    public static LevelAssetExtractionResult Extract(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(package.Files);
        var header = DlAssetReader.ReadHeader(source.HeaderBytes);
        var assetBytes = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes)
            : source.AssetWadBytes;
        var mobys = DlAssetReader.ReadModelDefinitions(source.HeaderBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = DlAssetReader.ReadModelDefinitions(source.HeaderBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = DlAssetReader.ReadShrubDefinitions(source.HeaderBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var knownOffsets = DlAssetReader.CollectKnownAssetOffsets(GameId.UYA, header, assetBytes.Length, mobys, ties, shrubs);
        var mipmaps = DlAssetReader.ReadMipmapDefinitions(
            source.HeaderBytes, header.GsRamOffset, Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStash = mipmaps.Skip(header.GsRamCount).ToArray();
        var mobyTextures = DlAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextures = DlAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextures = DlAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var assets = new List<ExtractedLevelAsset>(mobys.Count + ties.Count + shrubs.Count);
        var failures = 0;

        foreach (var definition in mobys)
        {
            try
            {
                Add(assets, FrontendAssetKind.Moby, definition.ModelId, definition.Index,
                    source.HeaderBytes.AsSpan(header.MobyModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    ReadTextures("moby", definition.TextureIds, mobyTextures, source.PaletteBytes, assetBytes,
                        header.TextureDataOffset, gsStash));
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
                    source.HeaderBytes.AsSpan(header.TieModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    ReadTextures("tie", definition.TextureIds, tieTextures, source.PaletteBytes, assetBytes,
                        header.TextureDataOffset, null));
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
                var textures = ReadTextures("shrub", definition.TextureIds, shrubTextures, source.PaletteBytes,
                    assetBytes, header.TextureDataOffset, null).ToList();
                if (definition.Width > 0 && definition.Height > 0 && definition.TextureId > 0)
                    textures.Add(new(1, DlAssetReader.BuildShrubBillboardTexture(definition, source.PaletteBytes).PifBytes));
                Add(assets, FrontendAssetKind.Shrub, definition.ModelId, definition.Index,
                    source.HeaderBytes.AsSpan(header.ShrubModelOffset + definition.Index * 0x30, 0x30).ToArray(),
                    DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets), textures);
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                failures++;
            }
        }
        return new(assets, failures);
    }

    private static IReadOnlyList<ExtractedLevelAssetTexture> ReadTextures(
        string family,
        byte[] textureIds,
        IReadOnlyList<DlAssetTextureDefinition> definitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<DlAssetMipmapDefinition>? gsStash)
    {
        var textures = new List<ExtractedLevelAssetTexture>();
        foreach (var textureId in textureIds)
        {
            if (textureId == byte.MaxValue) continue;
            try
            {
                if (textureId >= definitions.Count) throw new InvalidDataException($"{family} texture ID {textureId} is out of range.");
                var texture = DlAssetReader.BuildAssetTexture(
                    family, textures.Count, definitions[textureId], paletteBytes, assetBytes, textureDataOffset,
                    gsStash, isSwizzled: false, useTextureFlags: true);
                textures.Add(new(0, texture.PifBytes));
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                textures.Add(new(0, null, exception.Message));
            }
        }
        return textures;
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
        if (modelBytes.Length > 0) assets.Add(new(kind, classId, sourceIndex, definitionBytes, modelBytes, textures));
    }

    private static bool IsUnsupportedAsset(Exception exception) => exception is
        ArgumentException or InvalidDataException or IOException or NotSupportedException or OverflowException;
}
