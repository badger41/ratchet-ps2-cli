using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Level;

internal static class UyaLevelAssetExtractor
{
    public static LevelAssetExtractionResult Extract(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        return Extract(UyaLevelWadUnpacker.Unpack(levelWadBytes).Files);
    }

    public static LevelAssetExtractionResult Extract(IReadOnlyList<PackedFile> unpackedFiles)
    {
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(unpackedFiles);
        var header = LevelAssetReader.ReadHeader(source.HeaderBytes);
        var assetBytes = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes)
            : source.AssetWadBytes;
        var mobys = LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var knownOffsets = LevelAssetReader.CollectKnownAssetOffsets(
            header, assetBytes.Length, mobys, ties, shrubs, [header.SceneViewSize]);
        var mipmaps = LevelAssetReader.ReadMipmapDefinitions(
            source.HeaderBytes, header.GsRamOffset, Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStash = mipmaps.Skip(header.GsRamCount).ToArray();
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
                    source.HeaderBytes.AsSpan(header.TieModelOffset + definition.Index * 0x20, 0x20).ToArray(),
                    LevelAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets),
                    LevelAssetTextureExtractor.Extract("tie", definition.TextureIds, tieTextures, source.PaletteBytes, assetBytes,
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
                var textures = LevelAssetTextureExtractor.Extract("shrub", definition.TextureIds, shrubTextures, source.PaletteBytes,
                    assetBytes, header.TextureDataOffset, null, isSwizzled: false).ToList();
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
        var sky = LevelAssetReader.ReadAssetSlice(assetBytes, header.SkyOffset, knownOffsets);
        return new(assets, failures)
        {
            SkyboxBytes = sky.Length == 0 ? null : sky,
            SoundBanks = ReadSounds(unpackedFiles, source.HeaderBytes, header.SoundRemapOffset),
        };
    }

    private static IReadOnlyList<ExtractedLevelSoundBank> ReadSounds(
        IReadOnlyList<PackedFile> files, byte[] headerBytes, int remapOffset)
    {
        const string archive = "level_wad/sound.bnk";
        var bytes = files.SingleOrDefault(file => file.Path == archive)?.Bytes;
        if (bytes is not { Length: > 0 }) return [];
        var bank = Snd989SoundBankReader.Inspect(bytes);
        var mobys = remapOffset == 0 ? [] : UyaSoundRemapReader.Inspect(headerBytes, bank.Programs.Count).Mobys
            .Select(map => new ExtractedLevelMobySounds(map.DefinitionIndex, map.ClassId,
                map.Slots.Select(slot => slot.NativeProgramIndex).ToArray(), "level_wad/assets/asset_wad.bin"))
            .ToArray();
        return [new(bytes, archive, bank, mobys)];
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
