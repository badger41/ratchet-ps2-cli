using System.Buffers.Binary;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Ties;

internal static partial class UyaStaticTieBinaryWriter
{
    private static byte[] BuildRemapBytes(
        IReadOnlyList<PacketBuild> packets,
        IReadOnlyList<int> logicalNormalIndices)
    {
        var assignments = BuildRemapAssignments(packets, logicalNormalIndices);
        var groups = new List<RemapGroupBuild>();
        var groupAssignments = new List<RemapAssignment>();
        var groupFirstSlot = 0;
        var groupPacking = new UyaRemapGroupPackingState();
        var assignmentIndex = 0;
        foreach (var packet in packets)
        {
            if (groupPacking.HasPackets && !groupPacking.CanAppendPacket(packet.Vertices.Count))
                FlushGroup();
            for (var index = 0; index < packet.Vertices.Count; index++)
                groupAssignments.Add(assignments[assignmentIndex++]);
            groupPacking.AppendPacket(packet.Vertices.Count);
        }
        if (groupPacking.HasPackets)
            FlushGroup();
        if (groups.Count > MaximumRemapGroups)
            throw new InvalidDataException(
                $"UYA TIE normal/RGBA remaps need {groups.Count} groups, over the {MaximumRemapGroups}-group cache limit.");
        var bytes = new byte[checked(RemapGroupHeaderSize + groups.Sum(group => group.ByteLength))];
        var groupOffset = RemapGroupHeaderSize;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            bytes[groupIndex] = checked((byte)(group.ByteLength / 0x10));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(groupOffset), checked((ushort)(group.Assignments.Count * 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0a), checked((ushort)(group.ZeroTargetSlots.Count * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0c), checked((ushort)(group.SlotCount / 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0e), checked((ushort)group.ByteLength));
            for (var index = 0; index < group.Assignments.Count; index++)
            {
                var assignment = group.Assignments[index];
                var offset = groupOffset + 0x10 + index * 4;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset),
                    checked((ushort)(assignment.NormalIndex * 4)));
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2),
                    checked((ushort)((assignment.TargetSlot - group.FirstSlot) * 4)));
            }
            var zeroOffset = groupOffset + 0x10 + group.Assignments.Count * 4;
            for (var index = 0; index < group.ZeroTargetSlots.Count; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(zeroOffset + index * 2),
                    checked((ushort)(group.ZeroTargetSlots[index] * 4)));
            groupOffset += group.ByteLength;
        }
        return bytes;

        void FlushGroup()
        {
            var occupied = groupAssignments.Select(value => value.TargetSlot - groupFirstSlot).ToHashSet();
            var zeroTargetSlots = Enumerable.Range(0, groupPacking.SlotCount)
                .Where(value => !occupied.Contains(value)).ToArray();
            var byteLength = Align16(checked(0x10 + groupAssignments.Count * 4 + zeroTargetSlots.Length * 2));
            groups.Add(new(groupFirstSlot, groupPacking.SlotCount, groupAssignments.ToArray(), zeroTargetSlots, byteLength));
            groupFirstSlot += groupPacking.SlotCount;
            groupPacking.Reset();
            groupAssignments.Clear();
        }
    }

    internal static int CountRemapGroups(IEnumerable<int> packetVertexCounts)
    {
        var groups = 0;
        var packing = new UyaRemapGroupPackingState();
        foreach (var vertexCount in packetVertexCounts)
        {
            if (packing.HasPackets && !packing.CanAppendPacket(vertexCount))
            {
                groups++;
                packing.Reset();
            }
            packing.AppendPacket(vertexCount);
        }
        return groups + (packing.HasPackets ? 1 : 0);
    }

    private static IReadOnlyList<RemapAssignment> BuildRemapAssignments(
        IReadOnlyList<PacketBuild> packets,
        IReadOnlyList<int> logicalNormalIndices)
    {
        var assignments = new List<RemapAssignment>(logicalNormalIndices.Count);
        var cacheBase = 0;
        var logicalVertexIndex = 0;
        foreach (var packet in packets)
        {
            for (var vertexIndex = 0; vertexIndex < packet.Vertices.Count; vertexIndex++, logicalVertexIndex++)
                assignments.Add(new(logicalNormalIndices[logicalVertexIndex],
                    cacheBase + GetStorageVertexIndex(packet.StripVertexCounts, vertexIndex)));
            cacheBase += Align4(packet.Vertices.Count);
        }
        return assignments;
    }

    private static void ValidateRemap(
        byte[] bytes,
        TieClass tie,
        IReadOnlyList<int> logicalNormalIndices)
    {
        var chunkOffset = checked((int)tie.Header.VertexNormalsOffset + tie.Header.RgbaRemapOffsets[0]);
        var expected = new List<RemapAssignment>(logicalNormalIndices.Count);
        var cacheBase = 0;
        var logicalVertexIndex = 0;
        foreach (var packet in tie.PacketTables[0].Packets)
        {
            var stripVertexCounts = tie.PacketDataBlocks
                .Single(block => block.LodIndex == 0 && block.PacketIndex == packet.PacketIndex)
                .StripControls.Select(strip => (int)strip.TokenCount).ToArray();
            for (var vertexIndex = 0; vertexIndex < packet.RgbaCount; vertexIndex++, logicalVertexIndex++)
            {
                expected.Add(new(logicalNormalIndices[logicalVertexIndex],
                    cacheBase + GetStorageVertexIndex(stripVertexCounts, vertexIndex)));
            }
            cacheBase += Align4(packet.RgbaCount);
        }
        var actual = tie.RgbaRemapOperations
            .Where(operation => operation.LodIndex == 0)
            .OrderBy(operation => operation.GroupIndex)
            .ThenBy(operation => operation.OperationIndex)
            .ToArray();
        var actualTargets = actual
            .Select(operation => operation.GroupTargetSlotBase + operation.TargetSlot)
            .ToArray();
        if (actual.Length != expected.Count
            || actual.Any(operation => operation.Kind != TieRgbaRemapOperationKind.DirectCopy
                || operation.SourceSlots.Length != 1)
            || actualTargets.Distinct().Count() != actualTargets.Length)
            throw new InvalidDataException("Generated UYA TIE normal/RGBA remap does not cover its LOD0 cache.");
        var actualByTarget = actual.ToDictionary(
            operation => operation.GroupTargetSlotBase + operation.TargetSlot,
            operation => operation.SourceSlots[0]);
        if (expected.Any(assignment => !actualByTarget.TryGetValue(assignment.TargetSlot, out var normalIndex)
                || normalIndex != assignment.NormalIndex))
            throw new InvalidDataException("Generated UYA TIE normal/RGBA remap does not cover its LOD0 cache.");
        var groupCount = actual.Select(operation => operation.GroupIndex).Distinct().Count();
        var packetCacheBoundaries = new HashSet<int> { 0 };
        var packetCacheSlotCount = 0;
        foreach (var packet in tie.PacketTables[0].Packets)
        {
            packetCacheSlotCount += Align4(packet.RgbaCount);
            packetCacheBoundaries.Add(packetCacheSlotCount);
        }
        if (actual.Select(operation => operation.GroupTargetSlotBase).Distinct()
                .Any(groupBase => !packetCacheBoundaries.Contains(groupBase))
            || Enumerable.Range(0, groupCount).Any(index => bytes[chunkOffset + index] is 0 or > MaximumRemapGroupByteLength / 0x10)
            || Enumerable.Range(groupCount, RemapGroupHeaderSize - groupCount)
            .Any(index => bytes[chunkOffset + index] != 0))
            throw new InvalidDataException("Generated UYA TIE has an invalid normal/RGBA remap group layout.");
        var groupOffset = chunkOffset + RemapGroupHeaderSize;
        var outputSlotCount = 0;
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupByteLength = bytes[chunkOffset + groupIndex] * 0x10;
            var directByteCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset));
            var zeroByteCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0a));
            var outputQwordCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0c));
            var encodedByteLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(groupOffset + 0x0e));
            var targets = Enumerable.Range(0, directByteCount / 4)
                .Select(index => BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(groupOffset + 0x10 + index * 4 + 2)) / 4)
                .Concat(Enumerable.Range(0, zeroByteCount / 2).Select(index =>
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(
                        groupOffset + 0x10 + directByteCount + index * 2)) / 4))
                .Order()
                .ToArray();
            if (directByteCount % 4 != 0
                || zeroByteCount % 2 != 0
                || encodedByteLength != groupByteLength
                || targets.Distinct().Count() != targets.Length
                || !targets.SequenceEqual(Enumerable.Range(0, outputQwordCount * 4)))
                throw new InvalidDataException("Generated UYA TIE normal/RGBA remap group does not cover its padded output cache.");
            outputSlotCount += outputQwordCount * 4;
            groupOffset += groupByteLength;
        }
        if (outputSlotCount != packetCacheSlotCount)
            throw new InvalidDataException("Generated UYA TIE normal/RGBA remap output size changed during semantic reread.");
    }

}

internal sealed class UyaRemapGroupPackingState
{
    public int SlotCount { get; private set; }
    public int AssignmentCount { get; private set; }
    public bool HasPackets => SlotCount > 0;

    public bool CanAppendPacket(int vertexCount)
    {
        var packetSlotCount = Align4(vertexCount);
        var addedZeroCount = packetSlotCount - vertexCount;
        var byteLength = 0x10
            + checked((AssignmentCount + vertexCount) * 4)
            + checked((SlotCount - AssignmentCount + addedZeroCount) * 2);
        return SlotCount + packetSlotCount <= UyaStaticTieBinaryWriter.MaximumRemapSlotsPerGroup
            && byteLength <= UyaStaticTieBinaryWriter.MaximumRemapGroupByteLength;
    }

    public void AppendPacket(int vertexCount)
    {
        if (!CanAppendPacket(vertexCount))
            throw new InvalidDataException("A UYA TIE packet is too large for one normal/RGBA remap group.");
        AssignmentCount = checked(AssignmentCount + vertexCount);
        SlotCount = checked(SlotCount + Align4(vertexCount));
    }

    public void Reset()
    {
        SlotCount = 0;
        AssignmentCount = 0;
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
}
