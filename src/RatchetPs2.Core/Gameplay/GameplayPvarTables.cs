using System.Buffers.Binary;

namespace RatchetPs2.Core.Gameplay;

public sealed record GameplayPvarTables(
    byte[] MobyLinksBytes,
    byte[] TableBytes,
    byte[] DataBytes,
    byte[] RelativePointerBytes,
    IReadOnlyList<GameplayPvarTableEntry> Entries,
    IReadOnlyList<GameplayPvarRelativePointer> RelativePointers)
{
    public IReadOnlyList<GameplayPvarRelativePointer> MobyLinks { get; init; } = [];
}

public sealed record GameplayPvarTableEntry(
    int Index,
    int Offset,
    int Length,
    byte[] Data);

public sealed record GameplayPvarRelativePointer(int PvarIndex, int Offset);

public sealed record GameplayPvarBuildEntry(
    byte[] Data,
    IReadOnlyList<int> MobyLinkOffsets,
    IReadOnlyList<int> RelativePointerOffsets);

public static class GameplayPvarTableReader
{
    public static GameplayPvarTables? Read(
        IReadOnlyList<GameplayRawBlock> blocks,
        string gameName)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var mobyLinksBytes = FindPayload(blocks, "pvar_moby_links");
        var tableBytes = FindPayload(blocks, "pvar_table");
        var dataBytes = FindPayload(blocks, "pvar_data");
        var relativePointerBytes = FindPayload(blocks, "pvar_relative_pointers");
        if (mobyLinksBytes.Length == 0
            && tableBytes.Length == 0
            && dataBytes.Length == 0
            && relativePointerBytes.Length == 0)
        {
            return null;
        }

        var entries = ReadEntries(tableBytes, dataBytes, gameName);
        return new GameplayPvarTables(
            mobyLinksBytes,
            tableBytes,
            dataBytes,
            relativePointerBytes,
            entries,
            ReadFixups(relativePointerBytes, entries, gameName, "relative pointer"))
        {
            MobyLinks = ReadFixups(mobyLinksBytes, entries, gameName, "moby link"),
        };
    }

    private static byte[] FindPayload(IReadOnlyList<GameplayRawBlock> blocks, string semanticName) =>
        blocks.FirstOrDefault(block => block.SemanticName == semanticName)?.PayloadBytes ?? [];

    private static GameplayPvarTableEntry[] ReadEntries(
        byte[] tableBytes,
        byte[] dataBytes,
        string gameName)
    {
        const int entrySize = 8;
        if (tableBytes.Length % entrySize != 0)
        {
            throw new InvalidDataException($"{gameName} pvar table length must be divisible by 8.");
        }

        var entries = new GameplayPvarTableEntry[tableBytes.Length / entrySize];
        for (var index = 0; index < entries.Length; index++)
        {
            var entryBytes = tableBytes.AsSpan(index * entrySize, entrySize);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[..4]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[4..]);
            if (offset < 0 || length < 0 || offset > dataBytes.Length - length)
            {
                throw new InvalidDataException($"{gameName} pvar table entry {index} points outside pvar_data.");
            }

            entries[index] = new GameplayPvarTableEntry(
                index,
                offset,
                length,
                length == 0 ? [] : dataBytes.AsSpan(offset, length).ToArray());
        }

        return entries;
    }

    private static GameplayPvarRelativePointer[] ReadFixups(
        byte[] bytes,
        IReadOnlyList<GameplayPvarTableEntry> entries,
        string gameName,
        string description)
    {
        const int entrySize = 8;
        if (bytes.Length % entrySize != 0)
        {
            throw new InvalidDataException($"{gameName} pvar {description} table length must be divisible by 8.");
        }

        var pointers = new List<GameplayPvarRelativePointer>(bytes.Length / entrySize);
        for (var index = 0; index < bytes.Length / entrySize; index++)
        {
            var entryBytes = bytes.AsSpan(index * entrySize, entrySize);
            var pvarIndex = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[..4]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[4..]);
            if (pvarIndex == -1)
            {
                if (offset != -1)
                    throw new InvalidDataException($"{gameName} pvar {description} table has an invalid terminator.");
                break;
            }
            if (pvarIndex < 0 || pvarIndex >= entries.Count
                || offset < 0 || offset % 4 != 0 || offset > entries[pvarIndex].Length - 4)
                throw new InvalidDataException(
                    $"{gameName} pvar {description} entry {index} ({pvarIndex}, {offset}) points outside "
                    + $"its pvar ({(pvarIndex >= 0 && pvarIndex < entries.Count ? entries[pvarIndex].Length : -1)} bytes).");
            pointers.Add(new GameplayPvarRelativePointer(pvarIndex, offset));
        }

        if (pointers.Distinct().Count() != pointers.Count)
            throw new InvalidDataException($"{gameName} pvar {description} table contains duplicate entries.");

        return pointers.ToArray();
    }
}

public static class GameplayPvarTableWriter
{
    public static GameplayPvarTables Write(IReadOnlyList<GameplayPvarBuildEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tableLength = checked(source.Count * 8);
        var dataLength = 0;
        foreach (var entry in source)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Data);
            ValidateOffsets(entry.MobyLinkOffsets, entry.Data.Length, "moby link");
            ValidateOffsets(entry.RelativePointerOffsets, entry.Data.Length, "relative pointer");
            dataLength = checked(dataLength + entry.Data.Length);
        }

        var table = GC.AllocateUninitializedArray<byte>(tableLength);
        var data = GC.AllocateUninitializedArray<byte>(dataLength);
        var mobyLinks = new List<GameplayPvarRelativePointer>();
        var relativePointers = new List<GameplayPvarRelativePointer>();
        var dataOffset = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var entry = source[index];
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(index * 8), dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(index * 8 + 4), entry.Data.Length);
            entry.Data.CopyTo(data, dataOffset);
            mobyLinks.AddRange(entry.MobyLinkOffsets.Order().Select(offset =>
                new GameplayPvarRelativePointer(index, offset)));
            relativePointers.AddRange(entry.RelativePointerOffsets.Order().Select(offset =>
                new GameplayPvarRelativePointer(index, offset)));
            dataOffset += entry.Data.Length;
        }

        var mobyLinkBytes = WriteFixups(mobyLinks);
        var relativePointerBytes = WriteFixups(relativePointers);
        return new(mobyLinkBytes, table, data, relativePointerBytes,
            source.Select((entry, index) => new GameplayPvarTableEntry(
                index,
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(index * 8)),
                entry.Data.Length,
                entry.Data.ToArray())).ToArray(),
            relativePointers)
        {
            MobyLinks = mobyLinks,
        };
    }

    private static void ValidateOffsets(IReadOnlyList<int>? offsets, int length, string description)
    {
        if (offsets is null) throw new ArgumentException($"Pvar {description} offsets are required.");
        if (offsets.Distinct().Count() != offsets.Count)
            throw new ArgumentException($"Pvar {description} offsets must be unique.");
        if (offsets.Any(offset => offset < 0 || offset % 4 != 0 || offset > length - 4))
            throw new ArgumentException($"Pvar {description} offset points outside its pvar.");
    }

    private static byte[] WriteFixups(IReadOnlyList<GameplayPvarRelativePointer> values)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(checked((values.Count + 1) * 8));
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 8), values[index].PvarIndex);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 8 + 4), values[index].Offset);
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(values.Count * 8), -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(values.Count * 8 + 4), -1);
        return bytes;
    }
}
