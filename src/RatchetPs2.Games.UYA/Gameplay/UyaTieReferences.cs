using System.Buffers.Binary;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaTieGroups(IReadOnlyList<int[]> Groups);

public static class UyaTieGroupsReader
{
    public static UyaTieGroups Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, 0x10, "UYA tie groups header");
        var count = ReadInt32LittleEndian(data, 0);
        var dataSize = ReadInt32LittleEndian(data, 4);
        if (count < 0 || dataSize < 0) throw new InvalidDataException("UYA tie group sizes cannot be negative.");
        EnsureRange(data, 0x10, checked(count * sizeof(int)), "UYA tie group pointers");
        var dataOffset = Align16(checked(0x10 + count * sizeof(int)));
        EnsureRange(data, dataOffset, dataSize, "UYA tie group members");
        var groups = new int[count][];
        for (var index = 0; index < count; index++)
        {
            var pointer = ReadInt32LittleEndian(data, 0x10 + index * sizeof(int));
            if (pointer == -1)
            {
                groups[index] = [];
                continue;
            }
            if (pointer < 0 || pointer % sizeof(ushort) != 0 || pointer >= dataSize)
                throw new InvalidDataException($"UYA tie group {index} pointer is invalid.");
            var members = new List<int>();
            var offset = checked(dataOffset + pointer);
            while (true)
            {
                EnsureRange(data, offset, sizeof(ushort), $"UYA tie group {index} member");
                var member = ReadUInt16LittleEndian(data, offset);
                members.Add(member & 0x7fff);
                offset += sizeof(ushort);
                if ((member & 0x8000) != 0) break;
                if (offset >= dataOffset + dataSize)
                    throw new InvalidDataException($"UYA tie group {index} has no terminating member.");
            }
            groups[index] = members.ToArray();
        }
        return new(groups);
    }

    private static int Align16(int value) => checked((value + 0xf) & ~0xf);
}

public static class UyaTieGroupsWriter
{
    public static byte[] Remap(ReadOnlySpan<byte> source, IReadOnlyList<int> targetSourceIndices)
    {
        ArgumentNullException.ThrowIfNull(targetSourceIndices);
        var sourceGroups = UyaTieGroupsReader.Read(source).Groups;
        var groups = sourceGroups.Select(group =>
        {
            var members = group.ToHashSet();
            return targetSourceIndices.Select((sourceIndex, targetIndex) => (sourceIndex, targetIndex))
                .Where(value => members.Contains(value.sourceIndex))
                .Select(value => value.targetIndex)
                .ToArray();
        }).ToArray();
        var dataOffset = Align16(checked(0x10 + groups.Length * sizeof(int)));
        var memberBytes = groups.Sum(value => checked(value.Length * sizeof(ushort)));
        var dataSize = Align16(memberBytes);
        var output = new byte[checked(dataOffset + dataSize)];
        BinaryPrimitives.WriteInt32LittleEndian(output, groups.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), dataSize);
        var offset = dataOffset;
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x10 + index * sizeof(int)),
                group.Length == 0 ? -1 : offset - dataOffset);
            for (var memberIndex = 0; memberIndex < group.Length; memberIndex++)
            {
                var member = group[memberIndex];
                if (member is < 0 or > 0x7fff)
                    throw new InvalidDataException("UYA tie group member exceeds the 15-bit index limit.");
                BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset),
                    (ushort)(member | (memberIndex == group.Length - 1 ? 0x8000 : 0)));
                offset += sizeof(ushort);
            }
        }
        return output;
    }

    private static int Align16(int value) => checked((value + 0xf) & ~0xf);
}

public readonly record struct UyaOcclusionMapping(int BitIndex, int OcclusionId);

public sealed record UyaOcclusionMappings(
    IReadOnlyList<UyaOcclusionMapping> Tfrags,
    IReadOnlyList<UyaOcclusionMapping> Ties,
    IReadOnlyList<UyaOcclusionMapping> Mobys,
    int Padding,
    byte[] TrailingBytes);

public static class UyaOcclusionMappingsReader
{
    public static UyaOcclusionMappings Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, 0x10, "UYA occlusion mappings header");
        var tfragCount = ReadCount(data, 0, "tfrag");
        var tieCount = ReadCount(data, 4, "tie");
        var mobyCount = ReadCount(data, 8, "moby");
        var offset = 0x10;
        var tfrags = ReadMappings(data, ref offset, tfragCount, "tfrag");
        var ties = ReadMappings(data, ref offset, tieCount, "tie");
        var mobys = ReadMappings(data, ref offset, mobyCount, "moby");
        return new(tfrags, ties, mobys, ReadInt32LittleEndian(data, 0xc), data[offset..].ToArray());
    }

    private static int ReadCount(ReadOnlySpan<byte> data, int offset, string family)
    {
        var count = ReadInt32LittleEndian(data, offset);
        if (count < 0) throw new InvalidDataException($"UYA {family} occlusion mapping count cannot be negative.");
        return count;
    }

    private static UyaOcclusionMapping[] ReadMappings(
        ReadOnlySpan<byte> data,
        ref int offset,
        int count,
        string family)
    {
        EnsureRange(data, offset, checked(count * 8), $"UYA {family} occlusion mappings");
        var values = new UyaOcclusionMapping[count];
        for (var index = 0; index < count; index++, offset += 8)
            values[index] = new(ReadInt32LittleEndian(data, offset), ReadInt32LittleEndian(data, offset + 4));
        return values;
    }
}

public static class UyaOcclusionMappingsWriter
{
    public static byte[] RemapTies(
        ReadOnlySpan<byte> source,
        IReadOnlyList<int> targetOcclusionIds)
    {
        ArgumentNullException.ThrowIfNull(targetOcclusionIds);
        var mappings = UyaOcclusionMappingsReader.Read(source);
        var remaining = targetOcclusionIds.GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());
        var ties = new List<UyaOcclusionMapping>(targetOcclusionIds.Count);
        foreach (var mapping in mappings.Ties)
        {
            if (!remaining.Remove(mapping.OcclusionId, out var count)) continue;
            for (var index = 0; index < count; index++) ties.Add(mapping);
        }
        if (remaining.Count > 0)
            throw new InvalidDataException("UYA tie instance has no corresponding occlusion ID mapping.");
        var output = new byte[checked(0x10 + (mappings.Tfrags.Count + ties.Count + mappings.Mobys.Count) * 8
            + mappings.TrailingBytes.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(output, mappings.Tfrags.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), ties.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), mappings.Mobys.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0xc), mappings.Padding);
        var offset = 0x10;
        WriteMappings(output, ref offset, mappings.Tfrags);
        WriteMappings(output, ref offset, ties);
        WriteMappings(output, ref offset, mappings.Mobys);
        mappings.TrailingBytes.CopyTo(output, offset);
        return output;
    }

    private static void WriteMappings(Span<byte> output, ref int offset, IReadOnlyList<UyaOcclusionMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output[offset..], mapping.BitIndex);
            BinaryPrimitives.WriteInt32LittleEndian(output[(offset + 4)..], mapping.OcclusionId);
            offset += 8;
        }
    }
}
