using System.Buffers.Binary;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Sounds;

namespace RatchetPs2.Games.DL.Level;

internal static class DlLevelSoundExtractor
{
    public static IReadOnlyList<ExtractedLevelSoundBank> Read(byte[] wadBytes, DlLevelWad wad,
        byte[] headerBytes, IReadOnlyList<ExtractedLevelAsset> assets)
    {
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var remap = header.MobySoundRemapOffset > 0 ? Block(headerBytes, header.MobySoundRemapOffset, 0x220) : [];
        if (remap.Length > 0)
            remap = Block(headerBytes, header.MobySoundRemapOffset, Int(remap, 0));
        var banks = new List<ExtractedLevelSoundBank>();
        Add(wad.CoreBank, "level_wad/core_sound.bnk", "assets/asset_wad.bin", null);
        for (var mission = 0; mission < wad.MissionBanks.Count; mission++)
            Add(wad.MissionBanks[mission], $"missions/{mission:0000}/sound.bnk", $"missions/{mission:0000}/classes.bin", mission);
        // Single-player core mobys use a mission bank; the disc's core bank only contains menu sounds.
        var primary = banks.FindIndex(value => value.ArchivePath.StartsWith("missions/", StringComparison.Ordinal));
        if (primary < 0 && banks.Count > 0) primary = 0;
        if (primary >= 0)
            banks[primary] = banks[primary] with { Mobys = banks[primary].Mobys
                .Concat(ReadMappings("assets/asset_wad.bin", null)).ToArray() };
        return banks;

        void Add(DlFileBlock block, string archive, string modelArchive, int? mission)
        {
            var bytes = DlLevelWadReader.ReadSectorFileBlock(wadBytes, block);
            if (bytes.Length == 0) return;
            var bank = Snd989SoundBankReader.Inspect(bytes);
            banks.Add(new(bytes, archive, bank, mission is null ? [] : ReadMappings(modelArchive, mission)));
        }

        IReadOnlyList<ExtractedLevelMobySounds> ReadMappings(string modelArchive, int? mission)
        {
            var mappings = new List<ExtractedLevelMobySounds>();
            var count = remap.Length == 0 ? 0 : Int(remap, mission is { } index ? 0x20 + index * 4 : 0x10);
            var table = remap.Length == 0 ? 0 : mission is { } m
                ? Int(remap, checked(Int(remap, 8) + m * 4)) : Int(remap, 4);
            foreach (var asset in assets.Where(value => value.Kind == FrontendAssetKind.Moby
                && (value.SourceArchivePath ?? "assets/asset_wad.bin") == modelArchive))
            {
                var soundCount = asset.ModelBytes.Length >= 0x40 ? asset.ModelBytes[0x0d] : 0;
                if (soundCount == 0 || remap.Length == 0) continue;
                if ((uint)asset.SourceIndex >= (uint)count)
                    throw new InvalidDataException($"DL {modelArchive} sound remap has no model {asset.SourceIndex}.");
                var relative = Int(remap, checked(table + asset.SourceIndex * 4));
                if (relative == -1) continue;
                if (relative < 0 || relative % 4 != 0) throw new InvalidDataException("DL moby sound list offset is invalid.");
                var slots = new int[soundCount];
                var start = checked(Int(remap, 12) + relative);
                for (var slot = 0; slot < slots.Length; slot++)
                {
                    var program = Int(remap, checked(start + slot * 4));
                    if (program < -1)
                        throw new InvalidDataException($"DL {modelArchive} model {asset.SourceIndex} has invalid sound program {program}.");
                    slots[slot] = program;
                }
                mappings.Add(new(asset.SourceIndex, asset.ClassId, slots, modelArchive));
            }
            return mappings;
        }
    }

    private static int Int(byte[] bytes, int offset) => offset < 0 || offset > bytes.Length - 4
        ? throw new InvalidDataException("DL sound remap points outside its source block.")
        : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static byte[] Block(byte[] bytes, int offset, int length) => offset < 0 || length < 0 || offset > bytes.Length - length
        ? throw new InvalidDataException("DL sound remap points outside its source block.") : bytes.AsSpan(offset, length).ToArray();
}
