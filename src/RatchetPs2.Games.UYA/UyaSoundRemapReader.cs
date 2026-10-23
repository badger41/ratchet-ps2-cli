using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Sounds;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA;

internal static class UyaSoundRemapReader
{
    private const int HeaderSize = 8;
    private const int MobyConfigSize = 4;
    private const int OtherRecordSize = 0x20;

    public static SoundRemapInventory Inspect(
        ReadOnlySpan<byte> headerBytes,
        int programCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (programCount < 0) throw new ArgumentOutOfRangeException(nameof(programCount));
        var header = LevelAssetReader.ReadHeader(headerBytes);
        if (header.SoundRemapOffset <= 0 || header.SoundRemapOffset > headerBytes.Length - HeaderSize)
            throw new InvalidDataException("UYA asset header has no bounded sound-remap block.");

        var source = headerBytes[header.SoundRemapOffset..];
        var otherOffset = ReadNonNegativeInt16(source, 0, "other-record offset");
        var otherCount = ReadNonNegativeInt16(source, 2, "other-record count");
        var globalOffset = ReadNonNegativeInt16(source, 4, "global-list offset");
        var globalCount = ReadNonNegativeInt16(source, 6, "global-list count");
        if (otherOffset < HeaderSize || (otherOffset - HeaderSize) % MobyConfigSize != 0)
            throw new InvalidDataException("UYA sound-remap moby configuration table is invalid.");
        var configCount = (otherOffset - HeaderSize) / MobyConfigSize;
        if (configCount > header.MobyModelCount)
            throw new InvalidDataException("UYA sound remap has more moby configs than model definitions.");

        var definitions = LevelAssetReader.ReadModelDefinitions(
            headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var mobys = new SoundMobyRemapInfo[configCount];
        var end = HeaderSize + configCount * MobyConfigSize;
        for (var index = 0; index < configCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configOffset = HeaderSize + index * MobyConfigSize;
            var listOffset = ReadInt16LittleEndian(source, configOffset);
            var count = ReadInt16LittleEndian(source, configOffset + 2);
            if (count < 0 || count > 0 && listOffset <= 0)
                throw new InvalidDataException($"UYA sound-remap moby config {index} is invalid.");
            SoundSourceRange? slotRange = null;
            SoundRemapSlotInfo[] slots = [];
            if (count > 0)
            {
                var bytes = Slice(source, listOffset, checked(count * sizeof(int)), $"moby config {index} list");
                slots = ReadSlots(bytes, header.SoundRemapOffset + listOffset, programCount, nullable: true,
                    $"moby class 0x{definitions[index].ModelId:X4}");
                slotRange = new(header.SoundRemapOffset + listOffset, bytes.Length);
                end = Math.Max(end, checked(listOffset + bytes.Length));
            }
            mobys[index] = new(
                index,
                definitions[index].ModelId,
                listOffset,
                count,
                new(header.SoundRemapOffset + configOffset, MobyConfigSize),
                slotRange,
                slots);
        }

        var otherBytes = Slice(source, otherOffset, checked(otherCount * OtherRecordSize), "other records");
        var globalBytes = Slice(source, globalOffset, checked(globalCount * sizeof(int)), "global list");
        var globals = ReadSlots(globalBytes, header.SoundRemapOffset + globalOffset,
            programCount, nullable: false, "global list");
        end = Math.Max(end, checked(otherOffset + otherBytes.Length));
        end = Math.Max(end, checked(globalOffset + globalBytes.Length));
        return new(
            new(header.SoundRemapOffset, end),
            mobys,
            otherCount,
            new(header.SoundRemapOffset + otherOffset, otherBytes.Length),
            globals,
            new(header.SoundRemapOffset + globalOffset, globalBytes.Length));
    }

    private static SoundRemapSlotInfo[] ReadSlots(
        ReadOnlySpan<byte> bytes,
        int absoluteOffset,
        int programCount,
        bool nullable,
        string owner)
    {
        var result = new SoundRemapSlotInfo[bytes.Length / sizeof(int)];
        for (var index = 0; index < result.Length; index++)
        {
            var value = ReadInt32LittleEndian(bytes, index * sizeof(int));
            if (value >= programCount || value < 0 && !(nullable && value == -1))
                throw new InvalidDataException(
                    $"UYA sound-remap {owner} slot {index} references invalid program {value}.");
            result[index] = new(index, value, new(absoluteOffset + index * sizeof(int), sizeof(int)));
        }
        return result;
    }

    private static short ReadNonNegativeInt16(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = ReadInt16LittleEndian(bytes, offset);
        if (value < 0) throw new InvalidDataException($"UYA sound-remap {name} is negative.");
        return value;
    }

}
