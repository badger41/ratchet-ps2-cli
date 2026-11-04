using System.Buffers.Binary;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.GC.Level;

internal static class GcTieColorRemapWriter
{
    public static byte[] Write(byte[] source, TieClass tie, int lod, IReadOnlyList<GcTieRebuiltPacket> packets)
    {
        var recipes = tie.RgbaRemapOperations.Where(operation => operation.LodIndex == lod)
            .GroupBy(operation => operation.TargetCacheSlot).ToDictionary(group => group.Key,
                group => group.OrderBy(operation => operation.Offset).Last());
        var groups = new List<byte[]>();
        var colors = new List<int>();
        foreach (var packet in packets)
        {
            if (colors.Count > 0 && (colors.Count + packet.SourceColorSlots.Length > 256
                || GroupSize(colors.Concat(packet.SourceColorSlots), recipes) > 0x400)) Flush();
            colors.AddRange(packet.SourceColorSlots);
            if (colors.Count > 256 || GroupSize(colors, recipes) > 0x400)
                throw new InvalidDataException("GC tie color packet exceeds the UYA lighting scratch buffer.");
        }
        if (colors.Count > 0) Flush();
        if (groups.Count > 31) throw new InvalidDataException("GC tie needs more than 31 lighting remap groups.");
        var bytes = new byte[32 + groups.Sum(group => group.Length)];
        var offset = 32;
        for (var index = 0; index < groups.Count; index++)
        {
            bytes[index] = checked((byte)(groups[index].Length / 16));
            groups[index].CopyTo(bytes, offset);
            offset += groups[index].Length;
        }
        return bytes;

        void Flush()
        {
            var group = new byte[GroupSize(colors, recipes)];
            var cursor = 16;
            for (var kind = 0; kind < 6; kind++)
            {
                var start = cursor;
                for (var index = 0; index < colors.Count; index++)
                {
                    var color = colors[index];
                    if (color < 0)
                    {
                        if (kind != 5) continue;
                        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(cursor), checked((ushort)(index * 4)));
                        cursor += 2;
                        continue;
                    }
                    if (!recipes.TryGetValue(color, out var recipe))
                        throw new InvalidDataException($"GC LOD {lod} color slot {color} has no lighting recipe.");
                    if ((int)recipe.Kind != kind) continue;
                    var size = RecordSize(recipe.Kind);
                    source.AsSpan(recipe.Offset, size).CopyTo(group.AsSpan(cursor));
                    if (recipe.Kind is TieRgbaRemapOperationKind.Average2 or TieRgbaRemapOperationKind.WeightedAverage3To1)
                    {
                        var packed = BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(cursor));
                        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(cursor), (packed & ~0x3ffcu) | (uint)(index * 4));
                    }
                    else
                    {
                        var target = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(cursor + size - 2));
                        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(cursor + size - 2),
                            checked((ushort)((target & ~0x3ffc) | index * 4)));
                    }
                    cursor += size;
                }
                BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(kind * 2), checked((ushort)(cursor - start)));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(12), checked((ushort)(colors.Count / 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(14), checked((ushort)group.Length));
            groups.Add(group);
            colors.Clear();
        }
    }

    public static byte[]? WriteGlowTargets(byte[] source, TieClass tie, int lod, IReadOnlyList<GcTieRebuiltPacket> packets)
    {
        var relative = tie.Header.UnknownOffsets78[lod];
        if (relative == 0) return null;
        var offset = checked((int)tie.Header.VertexNormalsOffset + relative);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset));
        var oldTargets = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var target = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 2 + index * 2));
            if (target % 4 != 0) throw new InvalidDataException("GC glow-cache target is not word aligned.");
            oldTargets.Add(target / 4);
        }
        var targets = packets.SelectMany(packet => packet.SourceColorSlots)
            .Select((old, index) => (old, index)).Where(value => oldTargets.Contains(value.old)).ToArray();
        var bytes = new byte[Align16(2 + targets.Length * 2)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)targets.Length));
        for (var index = 0; index < targets.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 + index * 2), checked((ushort)(targets[index].index * 4)));
        return bytes;
    }

    private static int GroupSize(IEnumerable<int> colors, IReadOnlyDictionary<int, TieRgbaRemapOperation> recipes) =>
        Align16(16 + colors.Sum(color => color < 0 ? 2 : recipes.TryGetValue(color, out var recipe)
            ? RecordSize(recipe.Kind) : throw new InvalidDataException($"GC color slot {color} has no lighting recipe.")));

    private static int RecordSize(TieRgbaRemapOperationKind kind) => kind switch
    {
        TieRgbaRemapOperationKind.DirectCopy or TieRgbaRemapOperationKind.Average2
            or TieRgbaRemapOperationKind.WeightedAverage3To1 => 4,
        TieRgbaRemapOperationKind.WeightedAverage2To1To1 or TieRgbaRemapOperationKind.Average4 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int Align16(int value) => checked((value + 15) & ~15);
}
