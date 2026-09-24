using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadWriter
{
    public static byte[] Write(
        UyaLevelWadInventory inventory,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var effective = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        if (replacements is not null)
            foreach (var replacement in replacements) effective.Add(replacement.Key, replacement.Value);

        var containers = inventory.Containers.ToDictionary(container => container.Path, StringComparer.Ordinal);
        var knownPaths = inventory.Containers.SelectMany(container => container.Slots)
            .SelectMany(slot => slot.LogicalPaths).ToHashSet(StringComparer.Ordinal);
        foreach (var path in effective.Keys)
            if (!knownPaths.Contains(path)) throw new ArgumentException($"Unknown UYA level payload path: {path}", nameof(replacements));

        PromoteChild(containers, effective, "assets/asset_wad.bin", "assets/asset_wad.bin");
        PromoteChild(containers, effective, "gameplay/gameplay_core.bin", "gameplay/gameplay.bin");
        PromoteChild(containers, effective, "level_wad/level_data.wad", "level_wad/level_data.wad");

        if (!containers.TryGetValue("level_wad", out var root))
            throw new InvalidDataException("UYA level inventory is missing its level_wad root container.");
        return WriteContainerCore(root, effective);
    }

    public static byte[] WriteContainer(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (replacements is not null)
        {
            var knownPaths = container.Slots.SelectMany(slot => slot.LogicalPaths).ToHashSet(StringComparer.Ordinal);
            foreach (var path in replacements.Keys)
                if (!knownPaths.Contains(path)) throw new ArgumentException($"Unknown {container.Path} payload path: {path}", nameof(replacements));
        }
        return WriteContainerCore(container, replacements);
    }

    private static void PromoteChild(
        IReadOnlyDictionary<string, UyaContainerInventory> containers,
        Dictionary<string, ReadOnlyMemory<byte>> replacements,
        string childPath,
        string parentSlotPath)
    {
        if (!containers.TryGetValue(childPath, out var child)) return;
        var changed = child.Slots.SelectMany(slot => slot.LogicalPaths).Any(replacements.ContainsKey);
        if (!changed) return;
        if (replacements.ContainsKey(parentSlotPath))
            throw new ArgumentException($"Cannot replace {parentSlotPath} and one of its nested payloads together.", nameof(replacements));
        replacements.Add(parentSlotPath, WriteContainerCore(child, replacements));
    }

    private static byte[] WriteContainerCore(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements)
    {
        ValidateInventory(container);
        var slotsByPath = container.Slots.ToDictionary(slot => slot.Path, StringComparer.Ordinal);
        var resolved = container.Slots.ToDictionary(
            slot => slot.Path,
            slot => ResolveReplacement(container.Path, slot, replacements),
            StringComparer.Ordinal);
        var placements = new Dictionary<string, SlotPlacement>(StringComparer.Ordinal);
        var parts = new List<(int Offset, ReadOnlyMemory<byte> Bytes)>();
        var cursor = 0;

        foreach (var region in container.Regions)
        {
            if (region.Kind != UyaContainerRegionKind.Payload)
            {
                parts.Add((cursor, region.Bytes));
                cursor = AddLength(container.Path, cursor, region.Length);
                continue;
            }

            if (region.SlotPath is null || !slotsByPath.TryGetValue(region.SlotPath, out var slot))
                throw new InvalidDataException($"{container.Path} payload region {region.Path} has no matching slot.");
            AppendSlot(container.Path, slot, resolved[slot.Path], parts, placements, ref cursor);
        }

        foreach (var slot in container.Slots.Where(slot => slot.Length == 0 && resolved[slot.Path].HasValue))
            AppendSlot(container.Path, slot, resolved[slot.Path], parts, placements, ref cursor);

        var output = new byte[cursor];
        foreach (var part in parts) part.Bytes.Span.CopyTo(output.AsSpan(part.Offset));
        PatchHeader(container, placements, output);
        return output;
    }

    private static void AppendSlot(
        string containerPath,
        UyaContainerSlot slot,
        ResolvedReplacement replacement,
        ICollection<(int Offset, ReadOnlyMemory<byte> Bytes)> parts,
        IDictionary<string, SlotPlacement> placements,
        ref int cursor)
    {
        var bytes = replacement.HasValue ? replacement.Bytes : slot.Bytes;
        if (bytes.Length == 0)
        {
            placements.Add(slot.Path, new(slot, replacement.HasValue, false, 0, 0));
            return;
        }

        var offset = Align(containerPath, cursor, slot.Alignment);
        parts.Add((offset, bytes));
        var end = AddLength(containerPath, offset, bytes.Length);
        cursor = Align(containerPath, end, slot.Alignment);
        placements.Add(slot.Path, new(slot, replacement.HasValue, true, offset, cursor - offset));
    }

    private static ResolvedReplacement ResolveReplacement(
        string containerPath,
        UyaContainerSlot slot,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements)
    {
        var result = new ResolvedReplacement(false, default);
        if (replacements is null) return result;
        foreach (var path in slot.LogicalPaths)
        {
            if (!replacements.TryGetValue(path, out var bytes)) continue;
            if (result.HasValue && !result.Bytes.Span.SequenceEqual(bytes.Span))
                throw new ArgumentException($"{containerPath} aliases for {slot.Path} have conflicting replacements.", nameof(replacements));
            result = new(true, bytes);
        }
        return result;
    }

    private static void PatchHeader(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, SlotPlacement> placements,
        Span<byte> output)
    {
        switch (container.Path)
        {
            case "level_wad":
                PatchBlockHeader(container, placements, output, 0x10, UyaLevelConstants.SectorSize);
                break;
            case "level_wad/level_data.wad":
                PatchBlockHeader(container, placements, output, 0, 1);
                break;
            case "gameplay/gameplay_core.bin":
                PatchGameplayHeader(container, placements, output);
                break;
            case "assets/asset_wad.bin":
                break;
            default:
                throw new InvalidDataException($"Unsupported UYA container path: {container.Path}");
        }
    }

    private static void PatchBlockHeader(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, SlotPlacement> placements,
        Span<byte> output,
        int headerOffset,
        int unit)
    {
        for (var index = 0; index < container.Slots.Count; index++)
        {
            var slot = container.Slots[index];
            var placement = placements.TryGetValue(slot.Path, out var value)
                ? value
                : new SlotPlacement(slot, false, false, 0, 0);
            var (offset, length) = GetDeclaration(container.Path, placement, unit);
            WriteInt32(container.Path, output, headerOffset + (index * 8), offset);
            WriteInt32(container.Path, output, headerOffset + (index * 8) + 4, length);
        }
    }

    private static void PatchGameplayHeader(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, SlotPlacement> placements,
        Span<byte> output)
    {
        foreach (var block in UyaGameplayLayout.Core.Blocks)
        {
            var path = $"gameplay/core/{block.SemanticName}.bin";
            var slot = container.Slots.Single(candidate => candidate.LogicalPaths.Contains(path, StringComparer.Ordinal));
            var placement = placements.TryGetValue(slot.Path, out var value)
                ? value
                : new SlotPlacement(slot, false, false, 0, 0);
            var pointer = placement.Present ? placement.Offset : placement.Replaced ? 0 : slot.DeclaredOffset;
            WriteInt32(container.Path, output, block.HeaderOffset, pointer);
        }
    }

    private static (int Offset, int Length) GetDeclaration(
        string containerPath,
        SlotPlacement placement,
        int unit)
    {
        if (!placement.Present)
            return placement.Replaced ? (0, 0) : (placement.Slot.DeclaredOffset, placement.Slot.DeclaredLength);
        if (placement.Offset % unit != 0 || placement.Length % unit != 0)
            throw new InvalidDataException($"{containerPath} slot {placement.Slot.Path} is not aligned to 0x{unit:X} bytes.");
        return (placement.Offset / unit, placement.Length / unit);
    }

    private static void ValidateInventory(UyaContainerInventory container)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(container.Bytes.Span)).ToLowerInvariant();
        if (!string.Equals(sourceHash, container.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"{container.Path} source bytes no longer match the inventoried SHA-256.");
        var slotsByPath = container.Slots.ToDictionary(slot => slot.Path, StringComparer.Ordinal);
        var cursor = 0;
        foreach (var region in container.Regions)
        {
            if (region.Offset != cursor || region.Length < 0 || region.Bytes.Length != region.Length)
                throw new InvalidDataException($"{container.Path} region {region.Path} does not provide exact ordered coverage.");
            cursor = AddLength(container.Path, cursor, region.Length);
            if (cursor > container.Bytes.Length)
                throw new InvalidDataException($"{container.Path} region {region.Path} exceeds its source container.");
            if (region.Kind == UyaContainerRegionKind.Payload
                && (region.SlotPath is null || !slotsByPath.ContainsKey(region.SlotPath)))
                throw new InvalidDataException($"{container.Path} payload region {region.Path} has no matching slot.");
        }
        if (cursor != container.Bytes.Length)
            throw new InvalidDataException($"{container.Path} regions do not own every source byte.");

        foreach (var slot in container.Slots)
        {
            if (slot.Alignment <= 0 || slot.Length < 0 || slot.Bytes.Length != slot.Length)
                throw new InvalidDataException($"{container.Path} slot {slot.Path} has invalid size or alignment metadata.");
            var regionCount = container.Regions.Count(region => region.SlotPath == slot.Path);
            if (regionCount != (slot.Length == 0 ? 0 : 1))
                throw new InvalidDataException($"{container.Path} slot {slot.Path} has ambiguous region ownership.");
        }
    }

    private static int Align(string path, int value, int alignment)
    {
        if (alignment <= 0) throw new InvalidDataException($"{path} contains an invalid alignment.");
        var aligned = ((long)value + alignment - 1) / alignment * alignment;
        if (aligned > int.MaxValue) throw new InvalidDataException($"{path} output exceeds supported container size.");
        return (int)aligned;
    }

    private static int AddLength(string path, int offset, int length)
    {
        var end = (long)offset + length;
        if (length < 0 || end > int.MaxValue) throw new InvalidDataException($"{path} output exceeds supported container size.");
        return (int)end;
    }

    private static void WriteInt32(string path, Span<byte> output, int offset, int value)
    {
        if (offset < 0 || offset > output.Length - sizeof(int))
            throw new InvalidDataException($"{path} header is too short for its declared slot table.");
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(offset, sizeof(int)), value);
    }

    private readonly record struct ResolvedReplacement(bool HasValue, ReadOnlyMemory<byte> Bytes);

    private readonly record struct SlotPlacement(
        UyaContainerSlot Slot,
        bool Replaced,
        bool Present,
        int Offset,
        int Length);
}
