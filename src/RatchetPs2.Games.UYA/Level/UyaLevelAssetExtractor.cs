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
        var result = LevelAssetInventoryReader.Extract(source.HeaderBytes, source.PaletteBytes, source.AssetWadBytes);
        return result with
        {
            SoundBanks = ReadSounds(unpackedFiles, source.HeaderBytes, LevelAssetReader.ReadHeader(source.HeaderBytes).SoundRemapOffset),
        };
    }

    private static IReadOnlyList<ExtractedLevelSoundBank> ReadSounds(
        IReadOnlyList<PackedFile> files, byte[] headerBytes, int remapOffset)
    {
        const string archive = "level_wad/sound.bnk";
        var bytes = files.SingleOrDefault(file => file.Path == archive)?.Bytes;
        if (bytes is not { Length: > 0 }) return [];
        var bank = Snd989SoundBankReader.Inspect(bytes);
        var mobys = remapOffset == 0 ? [] : LevelSoundRemapReader.Inspect(headerBytes, bank.Programs.Count).Mobys
            .Select(map => new ExtractedLevelMobySounds(map.DefinitionIndex, map.ClassId,
                map.Slots.Select(slot => slot.NativeProgramIndex).ToArray(), "level_wad/assets/asset_wad.bin"))
            .ToArray();
        return [new(bytes, archive, bank, mobys)];
    }

}
