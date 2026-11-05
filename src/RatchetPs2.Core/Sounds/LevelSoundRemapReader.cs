using RatchetPs2.Core.LevelAssets;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Sounds;

public static class LevelSoundRemapReader
{
    private const int HeaderSize = 8;
    private const int MobyConfigSize = 4;
    private const int OtherRecordSize = 0x20;

    public static SoundRemapInventory Inspect(
        ReadOnlySpan<byte> headerBytes,
        int programCount,
        CancellationToken cancellationToken = default,
        IReadOnlyList<int>? secondaryProgramCounts = null, bool hasBankSelector = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (programCount < 0) throw new ArgumentOutOfRangeException(nameof(programCount));
        var header = LevelAssetReader.ReadHeader(headerBytes);
        if (header.SoundRemapOffset <= 0 || header.SoundRemapOffset > headerBytes.Length - HeaderSize)
            throw new InvalidDataException("Level asset header has no bounded sound-remap block.");

        var source = headerBytes[header.SoundRemapOffset..];
        var otherOffset = ReadNonNegativeInt16(source, 0, "other-record offset");
        var otherCount = ReadNonNegativeInt16(source, 2, "other-record count");
        var globalOffset = ReadNonNegativeInt16(source, 4, "global-list offset");
        var globalCount = ReadNonNegativeInt16(source, 6, "global-list count");
        if (otherOffset < HeaderSize || (otherOffset - HeaderSize) % MobyConfigSize != 0)
            throw new InvalidDataException("Level sound-remap moby configuration table is invalid.");
        var configCount = (otherOffset - HeaderSize) / MobyConfigSize;
        if (configCount > header.MobyModelCount)
            throw new InvalidDataException("Level sound remap has more moby configs than model definitions.");

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
                throw new InvalidDataException($"Level sound-remap moby config {index} is invalid.");
            SoundSourceRange? slotRange = null;
            SoundRemapSlotInfo[] slots = [];
            var bankIndex = -1;
            if (count > 0)
            {
                var bytes = Slice(source, listOffset, checked(count * sizeof(int)), $"moby config {index} list");
                // The native loader adds one to the leading bank selector; -1 selects the primary bank.
                bankIndex = hasBankSelector ? ReadInt32LittleEndian(bytes, 0) : -1;
                var selectedProgramCount = programCount;
                if (bankIndex != -1)
                {
                    if (secondaryProgramCounts is null)
                        throw new NotSupportedException("Level moby sound remaps using secondary banks are not supported.");
                    if ((uint)bankIndex >= (uint)secondaryProgramCounts.Count || secondaryProgramCounts[bankIndex] < 0)
                        throw new InvalidDataException("Level moby sound remap selects an unavailable bank.");
                    selectedProgramCount = secondaryProgramCounts[bankIndex];
                }
                var prefix = hasBankSelector ? sizeof(int) : 0;
                slots = ReadSlots(bytes[prefix..], header.SoundRemapOffset + listOffset + prefix, selectedProgramCount, nullable: true,
                    $"moby class 0x{definitions[index].ModelId:X4}");
                slotRange = new(header.SoundRemapOffset + listOffset + prefix, bytes.Length - prefix);
                end = Math.Max(end, checked(listOffset + bytes.Length));
            }
            mobys[index] = new(
                index,
                definitions[index].ModelId,
                listOffset,
                count,
                new(header.SoundRemapOffset + configOffset, MobyConfigSize),
                slotRange,
                slots) { BankIndex = bankIndex };
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
                    $"Level sound-remap {owner} slot {index} references invalid program {value}.");
            result[index] = new(index, value, new(absoluteOffset + index * sizeof(int), sizeof(int)));
        }
        return result;
    }

    private static short ReadNonNegativeInt16(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = ReadInt16LittleEndian(bytes, offset);
        if (value < 0) throw new InvalidDataException($"Level sound-remap {name} is negative.");
        return value;
    }

}
