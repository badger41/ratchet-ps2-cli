using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.RC1.Gameplay;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelWadUnpacker
{
    public static Rc1LevelWadPackage Unpack(ReadOnlySpan<byte> levelWadBytes)
    {
        var levelWad = Rc1LevelWadReader.ReadLevelWad(levelWadBytes);
        var files = new List<PackedFile>();

        AddFile(files, "level_wad/header.bin", levelWad.HeaderBytes);
        var levelDataBytes = AddSectorFile(files, "level_wad/level_data.wad", levelWadBytes, levelWad.Data);
        var gameplayNtsc = AddSectorFile(files, "gameplay/gameplay.bin", levelWadBytes, levelWad.GameplayNtsc);
        var gameplayPal = AddSectorFile(files, "gameplay/gameplay_pal.bin", levelWadBytes, levelWad.GameplayPal);
        AddSectorFile(files, "occlusion/occlusion.bin", levelWadBytes, levelWad.Occlusion);

        if (levelDataBytes.Length > 0)
        {
            AddLevelDataPayloads(files, levelDataBytes);
        }

        AddGameplayPayload(files, "gameplay/gameplay_core.bin", gameplayNtsc, unpackCore: true);
        AddGameplayPayload(files, "gameplay/gameplay_pal_core.bin", gameplayPal, unpackCore: false);

        return new Rc1LevelWadPackage(levelWad, files);
    }

    public static PackedFilePackage UnpackPacked(ReadOnlySpan<byte> levelWadBytes)
    {
        return Unpack(levelWadBytes).ToPackedPackage();
    }

    public static IReadOnlyList<PackedFile> UnpackLevelData(ReadOnlySpan<byte> levelDataBytes)
    {
        var files = new List<PackedFile>();
        var rawBytes = levelDataBytes.ToArray();
        AddFile(files, "level_wad/level_data.wad", rawBytes);
        AddLevelDataPayloads(files, rawBytes);
        return files;
    }

    private static void AddLevelDataPayloads(List<PackedFile> files, byte[] levelDataBytes)
    {
        var levelData = Rc1LevelWadReader.ReadLevelDataWad(levelDataBytes);
        AddFile(files, "level_data/header.bin", levelData.HeaderBytes);
        AddByteFile(files, "code/code.bin", levelDataBytes, levelData.Overlay);
        AddByteFile(files, "level_wad/sound.bnk", levelDataBytes, levelData.SoundBank);
        AddByteFile(files, "assets/asset_header.bin", levelDataBytes, levelData.CoreIndex);
        AddByteFile(files, "assets/palette.bin", levelDataBytes, levelData.GsRam);
        AddByteFile(files, "hud/header.bin", levelDataBytes, levelData.HudHeader);

        for (var i = 0; i < levelData.HudBanks.Count; i++)
        {
            AddByteFile(files, $"hud/bank{i}.bin", levelDataBytes, levelData.HudBanks[i]);
        }

        AddByteFile(files, "assets/asset_wad.bin", levelDataBytes, levelData.CoreData);
    }

    private static void AddGameplayPayload(
        List<PackedFile> files,
        string path,
        byte[] gameplayBytes,
        bool unpackCore)
    {
        if (gameplayBytes.Length == 0)
        {
            return;
        }

        var payloadBytes = BinaryMagic.IsWad(gameplayBytes)
            ? WadCompression.Decompress(gameplayBytes)
            : gameplayBytes;
        AddFile(files, path, payloadBytes);

        if (!unpackCore || payloadBytes.Length < Rc1Gameplay.CoreHeaderSize)
        {
            return;
        }

        var gameplay = Rc1Gameplay.ReadCore(payloadBytes);
        AddFile(files, "gameplay/core/header.bin", gameplay.HeaderBytes);
        foreach (var block in gameplay.Blocks)
        {
            if (block.SemanticName == "tie_instances" && block.PayloadBytes.Length > 0)
            {
                var converted = Rc1Gameplay.ConvertTieInstances(block.PayloadBytes);
                AddFile(files, "gameplay/core/tie_instances.bin", converted.Instances);
                AddFile(files, "gameplay/core/tie_ambient_rgbas.bin", converted.AmbientRgbas);
            }
            else
            {
                AddFile(files, $"gameplay/core/{block.SemanticName}.bin", block.PayloadBytes);
            }
        }
    }

    private static byte[] AddSectorFile(
        List<PackedFile> files,
        string path,
        ReadOnlySpan<byte> container,
        SectorRange block)
    {
        var bytes = Rc1LevelWadReader.ReadSectorFileBlock(container, block);
        AddFile(files, path, bytes);
        return bytes;
    }

    private static void AddByteFile(
        List<PackedFile> files,
        string path,
        ReadOnlySpan<byte> container,
        Rc1ByteRange block)
    {
        AddFile(files, path, Rc1LevelWadReader.ReadByteFileBlock(container, block));
    }

    private static void AddFile(List<PackedFile> files, string path, byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            files.Add(new PackedFile(path, bytes, "application/octet-stream"));
        }
    }
}
