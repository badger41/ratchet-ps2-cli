using System.Buffers.Binary;

namespace RatchetPs2.Core.Gameplay;

public sealed record GameplayPvarTables(
    byte[] MobyLinksBytes,
    byte[] TableBytes,
    byte[] DataBytes,
    byte[] RelativePointerBytes,
    IReadOnlyList<GameplayPvarTableEntry> Entries,
    IReadOnlyList<GameplayPvarRelativePointer> RelativePointers);

public sealed record GameplayPvarTableEntry(
    int Index,
    int Offset,
    int Length,
    byte[] Data);

public sealed record GameplayPvarRelativePointer(int PvarIndex, int Offset);

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

        return new GameplayPvarTables(
            mobyLinksBytes,
            tableBytes,
            dataBytes,
            relativePointerBytes,
            ReadEntries(tableBytes, dataBytes, gameName),
            ReadRelativePointers(relativePointerBytes, gameName));
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

    private static GameplayPvarRelativePointer[] ReadRelativePointers(
        byte[] bytes,
        string gameName)
    {
        const int entrySize = 8;
        if (bytes.Length % entrySize != 0)
        {
            throw new InvalidDataException($"{gameName} pvar relative pointer table length must be divisible by 8.");
        }

        var pointers = new GameplayPvarRelativePointer[bytes.Length / entrySize];
        for (var index = 0; index < pointers.Length; index++)
        {
            var entryBytes = bytes.AsSpan(index * entrySize, entrySize);
            pointers[index] = new GameplayPvarRelativePointer(
                BinaryPrimitives.ReadInt32LittleEndian(entryBytes[..4]),
                BinaryPrimitives.ReadInt32LittleEndian(entryBytes[4..]));
        }

        return pointers;
    }
}
