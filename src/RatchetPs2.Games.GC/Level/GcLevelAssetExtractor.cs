using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.UYA;

namespace RatchetPs2.Games.GC.Level;

internal static class GcLevelAssetExtractor
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
        var banks = new List<(int Index, string Archive, byte[] Bytes, SoundBankInventory Inventory)>();
        Add(-1, "level_wad/sound.bnk");
        for (var index = 0; index < 3; index++) Add(index, $"level_wad/chunks/chunk{index}_bank.wad");
        if (banks.Count == 0) return [];
        var secondaryCounts = Enumerable.Range(0, 3)
            .Select(index => banks.Where(bank => bank.Index == index).Select(bank => bank.Inventory.Programs.Count).DefaultIfEmpty(-1).Single()).ToArray();
        var remaps = remapOffset == 0 ? [] : LevelSoundRemapReader.Inspect(headerBytes,
            banks.Where(bank => bank.Index == -1).Select(bank => bank.Inventory.Programs.Count).DefaultIfEmpty(0).Single(),
            secondaryProgramCounts: secondaryCounts).Mobys;
        return banks.Select(bank => new ExtractedLevelSoundBank(bank.Bytes, bank.Archive, bank.Inventory,
            remaps.Where(map => map.BankIndex == bank.Index).Select(map => new ExtractedLevelMobySounds(
                map.DefinitionIndex, map.ClassId, map.Slots.Select(slot => slot.NativeProgramIndex).ToArray(),
                "level_wad/assets/asset_wad.bin")).ToArray())).ToArray();

        void Add(int index, string archive)
        {
            var bytes = files.SingleOrDefault(file => file.Path == archive)?.Bytes;
            if (bytes is { Length: > 0 }) banks.Add((index, archive, bytes, Snd989SoundBankReader.Inspect(bytes)));
        }
    }

}
