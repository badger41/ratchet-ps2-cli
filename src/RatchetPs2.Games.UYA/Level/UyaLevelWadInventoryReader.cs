using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadInventoryReader
{
    public static UyaLevelWadInventory Read(ReadOnlyMemory<byte> levelWadBytes)
    {
        var levelWad = UyaLevelWadReader.ReadLevelWad(levelWadBytes.Span);
        var rootSlots = BuildRootSlots(levelWad);
        var root = BuildContainer(
            "level_wad",
            null,
            0,
            levelWadBytes.Length,
            UyaContainerCompression.None,
            levelWadBytes,
            levelWad.HeaderSize,
            Align(levelWad.HeaderSize, UyaLevelConstants.SectorSize),
            rootSlots);
        var containers = new List<UyaContainerInventory> { root };

        var levelDataSlot = FindSlot(root, "level_wad/level_data.wad");
        if (levelDataSlot.Length > 0)
        {
            var levelData = UyaLevelWadReader.ReadLevelDataWad(levelDataSlot.Bytes.Span);
            var levelDataContainer = BuildContainer(
                levelDataSlot.Path,
                root.Path,
                levelDataSlot.Offset,
                levelDataSlot.Length,
                levelDataSlot.Compression,
                levelDataSlot.Bytes,
                levelData.HeaderSize,
                levelData.HeaderSize,
                BuildLevelDataSlots(levelData));
            containers.Add(levelDataContainer);
            AddAssetContainer(containers, levelDataContainer);
        }

        var gameplaySlot = FindSlot(root, "gameplay/gameplay.bin");
        if (gameplaySlot.Length > 0)
        {
            var gameplayBytes = Decode(gameplaySlot);
            containers.Add(BuildContainer(
                "gameplay/gameplay_core.bin",
                root.Path,
                gameplaySlot.Offset,
                gameplaySlot.Length,
                gameplaySlot.Compression,
                gameplayBytes,
                UyaGameplayLayout.CoreHeaderSize,
                UyaGameplayLayout.CoreHeaderSize,
                BuildGameplaySlots(gameplayBytes.Span)));
        }

        return new(levelWad, containers);
    }

    private static IReadOnlyList<SlotDefinition> BuildRootSlots(UyaLevelWad value)
    {
        var slots = new List<SlotDefinition>
        {
            SectorSlot("level_wad/level_data.wad", value.Data),
            SectorSlot("level_wad/sound.bnk", value.SoundBank),
            SectorSlot("gameplay/gameplay.bin", value.Gameplay),
            SectorSlot("occlusion/occlusion.bin", value.Occlusion),
        };
        for (var index = 0; index < value.Chunks.Count; index++)
            slots.Add(SectorSlot($"level_wad/chunks/chunk{index}.wad", value.Chunks[index]));
        for (var index = 0; index < value.ChunkBanks.Count; index++)
            slots.Add(SectorSlot($"level_wad/chunks/chunk{index}_bank.wad", value.ChunkBanks[index]));
        return slots;
    }

    private static IReadOnlyList<SlotDefinition> BuildLevelDataSlots(UyaLevelDataWad value)
    {
        var slots = new List<SlotDefinition>
        {
            ByteSlot("code/code.bin", value.Overlay),
            ByteSlot("assets/asset_header.bin", value.CoreIndex),
            ByteSlot("assets/palette.bin", value.GsRam),
            ByteSlot("hud/header.bin", value.HudHeader),
        };
        for (var index = 0; index < value.HudBanks.Count; index++)
            slots.Add(ByteSlot($"hud/bank{index}.bin", value.HudBanks[index]));
        slots.Add(ByteSlot("assets/asset_wad.bin", value.CoreData));
        slots.Add(ByteSlot("transition_textures/transition_textures.bin", value.TransitionTextures));
        return slots;
    }

    private static IReadOnlyList<SlotDefinition> BuildGameplaySlots(ReadOnlySpan<byte> bytes)
    {
        var byteLength = bytes.Length;
        if (byteLength < UyaGameplayLayout.CoreHeaderSize)
            throw new InvalidDataException("UYA core gameplay is shorter than its pointer table.");

        var pointers = new List<(string SemanticName, int Pointer)>(UyaGameplayLayout.Core.Blocks.Count);
        foreach (var block in UyaGameplayLayout.Core.Blocks)
        {
            var pointer = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(block.HeaderOffset, sizeof(int)));
            if (pointer < 0 || pointer > byteLength)
                throw new InvalidDataException(
                    $"UYA core gameplay slot 0x{block.HeaderOffset:X2} points outside gameplay bounds.");
            pointers.Add((block.SemanticName, pointer));
        }
        var starts = pointers.Where(value => value.Pointer > 0).Select(value => value.Pointer).Distinct().Order().ToArray();
        var endsByStart = starts.Select((start, index) =>
            (Start: start, End: index + 1 < starts.Length ? starts[index + 1] : byteLength)).ToDictionary(
                value => value.Start,
                value => value.End);
        var pathsByPointer = pointers.Where(value => value.Pointer > 0).GroupBy(value => value.Pointer).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group.Select(value => $"gameplay/core/{value.SemanticName}.bin").ToArray());
        var slots = new List<SlotDefinition>();
        var emittedPointers = new HashSet<int>();
        foreach (var pointer in pointers)
        {
            var path = $"gameplay/core/{pointer.SemanticName}.bin";
            var alignment = pointer.SemanticName == "occlusion" ? 0x40 : 0x10;
            if (pointer.Pointer == 0)
            {
                slots.Add(new(path, [path], 0, 0, 0, 0, alignment));
                continue;
            }
            if (!emittedPointers.Add(pointer.Pointer)) continue;
            var paths = pathsByPointer[pointer.Pointer];
            alignment = paths.Contains("gameplay/core/occlusion.bin", StringComparer.Ordinal) ? 0x40 : 0x10;
            slots.Add(new(path, paths, pointer.Pointer, endsByStart[pointer.Pointer] - pointer.Pointer,
                pointer.Pointer, endsByStart[pointer.Pointer] - pointer.Pointer, alignment));
        }
        return slots;
    }

    private static void AddAssetContainer(
        ICollection<UyaContainerInventory> containers,
        UyaContainerInventory levelData)
    {
        var asset = FindSlot(levelData, "assets/asset_wad.bin");
        if (asset.Length == 0) return;
        var bytes = Decode(asset);
        containers.Add(BuildContainer(
            asset.Path,
            levelData.Path,
            asset.Offset,
            asset.Length,
            asset.Compression,
            bytes,
            0,
            0,
            [new("assets/asset_wad_payload.bin", ["assets/asset_wad_payload.bin"], 0, bytes.Length,
                0, bytes.Length, 1)]));
    }

    private static UyaContainerInventory BuildContainer(
        string path,
        string? sourceContainerPath,
        int sourceOffset,
        int sourceLength,
        UyaContainerCompression sourceCompression,
        ReadOnlyMemory<byte> bytes,
        int headerLength,
        int paddedHeaderLength,
        IReadOnlyList<SlotDefinition> definitions)
    {
        if (headerLength < 0 || paddedHeaderLength < headerLength || paddedHeaderLength > bytes.Length)
            throw new InvalidDataException($"{path} header bounds are invalid.");

        var slots = definitions.Select(definition => CreateSlot(path, bytes, definition)).ToArray();
        var payloads = slots.Where(slot => slot.Length > 0).OrderBy(slot => slot.Offset).ToArray();
        var regions = new List<UyaContainerRegion>();
        if (headerLength > 0)
            regions.Add(Region($"{path}/header", 0, headerLength, UyaContainerRegionKind.Header, null, bytes));
        if (paddedHeaderLength > headerLength)
            regions.Add(Region($"{path}/header-padding", headerLength, paddedHeaderLength - headerLength,
                UyaContainerRegionKind.Padding, null, bytes));

        var cursor = paddedHeaderLength;
        foreach (var slot in payloads)
        {
            if (slot.Offset < cursor)
                throw new InvalidDataException(
                    $"{path} slot {slot.Path} range 0x{slot.Offset:X}-0x{slot.Offset + slot.Length:X} overlaps another region.");
            if (slot.Offset > cursor)
                regions.Add(Region($"{path}/opaque-{cursor:X8}", cursor, slot.Offset - cursor,
                    UyaContainerRegionKind.Opaque, null, bytes));
            regions.Add(Region(slot.Path, slot.Offset, slot.Length,
                UyaContainerRegionKind.Payload, slot.Path, bytes));
            cursor = checked(slot.Offset + slot.Length);
        }
        if (cursor < bytes.Length)
            regions.Add(Region($"{path}/opaque-{cursor:X8}", cursor, bytes.Length - cursor,
                UyaContainerRegionKind.Opaque, null, bytes));

        return new(
            path,
            sourceContainerPath,
            sourceOffset,
            sourceLength,
            sourceCompression,
            Hash(bytes),
            bytes,
            slots,
            regions);
    }

    private static UyaContainerSlot CreateSlot(
        string containerPath,
        ReadOnlyMemory<byte> bytes,
        SlotDefinition definition)
    {
        if (definition.Offset < 0 || definition.Length < 0)
            throw new InvalidDataException($"{containerPath} slot {definition.Path} has a negative range.");
        var end = checked((long)definition.Offset + definition.Length);
        if (definition.Offset > bytes.Length || end > bytes.Length)
            throw new InvalidDataException(
                $"{containerPath} slot {definition.Path} range 0x{definition.Offset:X}-0x{end:X} exceeds length 0x{bytes.Length:X}.");
        var slice = bytes.Slice(definition.Offset, definition.Length);
        return new(
            definition.Path,
            definition.LogicalPaths,
            definition.DeclaredOffset,
            definition.DeclaredLength,
            definition.Offset,
            definition.Length,
            definition.Alignment,
            BinaryMagic.IsWad(slice.Span) ? UyaContainerCompression.Wad : UyaContainerCompression.None,
            Hash(slice),
            slice);
    }

    private static UyaContainerRegion Region(
        string path,
        int offset,
        int length,
        UyaContainerRegionKind kind,
        string? slotPath,
        ReadOnlyMemory<byte> bytes) =>
        new(path, offset, length, kind, slotPath, bytes.Slice(offset, length));

    private static UyaContainerSlot FindSlot(UyaContainerInventory container, string logicalPath) =>
        container.Slots.Single(slot => slot.LogicalPaths.Contains(logicalPath, StringComparer.Ordinal));

    private static ReadOnlyMemory<byte> Decode(UyaContainerSlot slot) =>
        slot.Compression == UyaContainerCompression.Wad
            ? WadCompression.Decompress(slot.Bytes.Span)
            : slot.Bytes;

    private static SlotDefinition SectorSlot(string path, UyaFileBlock block)
    {
        if (block.Length < 0 || block.Offset < -1 || block.Length > 0 && block.Offset < 0)
            throw new InvalidDataException($"{path} has an invalid sector range.");
        var offset = block.Length == 0 ? 0 : block.OffsetBytes;
        var length = block.SectorLengthBytes;
        if (offset > int.MaxValue || length > int.MaxValue)
            throw new InvalidDataException(
                $"{path} sector range ({block.Offset}, {block.Length}) exceeds supported container size.");
        return new(
            path,
            [path],
            block.Offset,
            block.Length,
            (int)offset,
            (int)length,
            UyaLevelConstants.SectorSize);
    }

    private static SlotDefinition ByteSlot(string path, UyaByteBlock block)
    {
        if (block.Length < 0 || block.Offset < -1 || block.Length > 0 && block.Offset < 0)
            throw new InvalidDataException($"{path} has an invalid byte range.");
        return new(path, [path], block.Offset, block.Length, block.Length == 0 ? 0 : block.Offset, block.Length, 1);
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);

    private static string Hash(ReadOnlyMemory<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();

    private sealed record SlotDefinition(
        string Path,
        IReadOnlyList<string> LogicalPaths,
        int DeclaredOffset,
        int DeclaredLength,
        int Offset,
        int Length,
        int Alignment);
}
