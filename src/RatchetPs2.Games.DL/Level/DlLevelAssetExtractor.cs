using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Games.DL.Level;

internal static class DlLevelAssetExtractor
{
    public static LevelAssetExtractionResult Extract(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        var wad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
        var core = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, wad.Data);
        var segments = DlCoreLevelSegmentReader.Read(core).ToDictionary(segment => segment.HeaderOffset, segment => segment.PayloadBytes);
        var source = new
        {
            HeaderBytes = segments.GetValueOrDefault(0x10)
                ?? throw new InvalidDataException("DL level is missing its asset header."),
            PaletteBytes = segments.GetValueOrDefault(0x18) ?? [],
            AssetWadBytes = segments.GetValueOrDefault(0x50)
                ?? throw new InvalidDataException("DL level is missing its asset WAD."),
        };
        var header = LevelAssetReader.ReadHeader(source.HeaderBytes);
        var assetBytes = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes)
            : source.AssetWadBytes;
        var mobys = LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var knownOffsets = LevelAssetReader.CollectKnownAssetOffsets(
            header, assetBytes.Length, mobys, ties, shrubs, [header.LightCuboidsOffset]);
        var mipmaps = LevelAssetReader.ReadMipmapDefinitions(
            source.HeaderBytes, header.GsRamOffset, Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStash = mipmaps.Skip(header.GsRamCount).ToArray();
        var gsStashClassIds = LevelAssetReader.ReadMobyGsStashClassIds(source.HeaderBytes, header.MobyGsStashListOffset);
        var mobyTextures = LevelAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextures = LevelAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextures = LevelAssetReader.ReadTextureDefinitions(
            source.HeaderBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var assets = new List<ExtractedLevelAsset>(mobys.Count + ties.Count + shrubs.Count);
        var failures = 0;

        foreach (var definition in mobys)
        {
            try
            {
                Add(assets, FrontendAssetKind.Moby, definition.ModelId, definition.Index,
                    source.HeaderBytes.AsSpan(header.MobyModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    LevelAssetTextureExtractor.Extract("moby", definition.TextureIds, mobyTextures, source.PaletteBytes, assetBytes,
                        header.TextureDataOffset, gsStash, !gsStashClassIds.Contains(definition.ModelId)));
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
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    LevelAssetTextureExtractor.Extract("tie", definition.TextureIds, tieTextures, source.PaletteBytes, assetBytes,
                        header.TextureDataOffset, null, isSwizzled: true));
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
                var textures = LevelAssetTextureExtractor.Extract("shrub", definition.TextureIds, shrubTextures, source.PaletteBytes,
                    assetBytes, header.TextureDataOffset, null, isSwizzled: true).ToList();
                if (definition.Width > 0 && definition.Height > 0 && definition.TextureId > 0)
                    textures.Add(new(1, LevelAssetReader.BuildShrubBillboardTexture(definition, source.PaletteBytes).PifBytes));
                Add(assets, FrontendAssetKind.Shrub, definition.ModelId, definition.Index,
                    source.HeaderBytes.AsSpan(header.ShrubModelOffset + definition.Index * 0x30, 0x30).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets), textures);
            }
            catch (Exception exception) when (IsUnsupportedAsset(exception))
            {
                failures++;
            }
        }
        for (var mission = 0; mission < wad.GameplayMissionData.Count; mission++)
        {
            var missionBytes = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, wad.GameplayMissionData[mission]);
            var classes = DlMissionDataReader.ReadClasses(missionBytes);
            if (classes.Length == 0) continue;
            foreach (var moby in DlMissionMobyBankReader.Read(classes))
            {
                var definition = new byte[0x20];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(definition.AsSpan(4), moby.Definition.ClassId);
                assets.Add(new(FrontendAssetKind.Moby, moby.Definition.ClassId,
                    moby.Definition.Index, definition, moby.ModelBytes,
                    moby.PifTextures.Select(bytes => new ExtractedLevelAssetTexture(0, bytes)).ToArray(),
                    $"missions/{mission:0000}/classes.bin"));
            }
        }
        return new(assets, failures)
        {
            SkyboxBytes = header.SkyOffset > 0
                ? LevelAssetReader.ReadAssetSlice(assetBytes, header.SkyOffset, knownOffsets) : null,
            SoundBanks = DlLevelSoundExtractor.Read(levelWadBytes, wad, source.HeaderBytes, assets),
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
