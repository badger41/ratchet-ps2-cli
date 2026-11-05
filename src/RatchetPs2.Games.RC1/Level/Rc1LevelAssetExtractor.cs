using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.RC1.Level;

internal static class Rc1LevelAssetExtractor
{
    public static LevelAssetExtractionResult Extract(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Extract(Rc1LevelWadUnpacker.Unpack(bytes).Files);
    }

    public static LevelAssetExtractionResult Extract(IReadOnlyList<PackedFile> files)
    {
        var source = Rc1LevelWadRenderPackageBuilder.GetAssetSources(files);
        var result = LevelAssetInventoryReader.Extract(source.HeaderBytes, source.PaletteBytes,
            source.AssetWadBytes, Rc1LevelAssetProfile.Default);
        var bank = files.SingleOrDefault(file => file.Path == "level_wad/sound.bnk")?.Bytes;
        if (bank is not { Length: > 0 }) return result;
        var inventory = Snd989SoundBankReader.Inspect(bank);
        var remaps = LevelAssetReader.ReadHeader(source.HeaderBytes).SoundRemapOffset == 0 ? []
            : LevelSoundRemapReader.Inspect(source.HeaderBytes, inventory.Programs.Count, hasBankSelector: false).Mobys;
        return result with { SoundBanks = [new(bank, "level_wad/sound.bnk", inventory,
            remaps.Select(moby => new ExtractedLevelMobySounds(moby.DefinitionIndex, moby.ClassId,
                moby.Slots.Select(slot => slot.NativeProgramIndex).ToArray(), "assets/asset_wad.bin")).ToArray())] };
    }
}
