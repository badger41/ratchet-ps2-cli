using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Sdk;

internal static class UyaOcclusionCompactionTests
{
    public static void Run()
    {
        var source = Grid([0, 1, 2], 2);
        var masks = Masks((0, [1]), (1, [1, 2]), (2, [80, 81, 82, 83]));
        var before = masks.ToDictionary(p => p.Key, p => p.Value.ToArray());
        var sourceCopy = source.ToArray();
        var raw = OcclusionComposer.ReplaceMasks(GameId.UYA, source, masks);
        Check(raw.Length > source.Length, "fixture requires compaction");
        var output = OcclusionComposer.RebuildGrid(GameId.UYA, source, masks);
        var decoded = Decode(output);
        Check(output.Length <= source.Length && decoded[(0, 0, 0)].SequenceEqual(Mask(1, 2))
            && decoded[(1, 0, 0)].SequenceEqual(Mask(1, 2))
            && decoded[(2, 0, 0)].SequenceEqual(masks[(2, 0, 0)]), "cheapest adjacent union wins");
        Preserved(masks, decoded);
        Check(source.SequenceEqual(sourceCopy) && masks.All(p => p.Value.SequenceEqual(before[p.Key])), "inputs are immutable");
        var reversed = masks.Reverse().ToDictionary(p => p.Key, p => p.Value);
        Check(output.SequenceEqual(OcclusionComposer.RebuildGrid(GameId.UYA, source, reversed)), "dictionary order does not affect output");
        Check(output.SequenceEqual(OcclusionComposer.CompactGrid(GameId.UYA, source, raw)), "existing raw caches use the same compaction");
        Check(output.SequenceEqual(OcclusionComposer.CompactGrid(GameId.UYA, source, output)), "compaction is idempotent");

        // Reused masks must pay for every cell affected by a merge, not just one mask slot.
        var weighted = Masks((0, [1]), (1, [1]), (2, [1]), (3, [1, 2]), (4, [1, 2, 3]));
        var weightedOutput = Decode(OcclusionComposer.RebuildGrid(GameId.UYA, Grid([0, 1, 2, 3, 4], 2), weighted));
        Check(weightedOutput[(0, 0, 0)].SequenceEqual(Mask(1))
            && weightedOutput[(3, 0, 0)].SequenceEqual(Mask(1, 2, 3)), "merge cost counts affected octants");
        Preserved(weighted, weightedOutput);

        // The cheapest new union can already exist in another part of the grid.
        var collision = Masks((0, [1]), (1, [2]), (3, [1, 2]), (4, [1, 2]), (5, [1, 2]));
        var collisionGrid = OcclusionComposer.RebuildGrid(GameId.UYA, Grid([0, 1, 3, 4, 5], 2), collision);
        Check(UyaOcclusionGridReader.Read(collisionGrid).Octants.Select(c => c.MaskIndex).Distinct().Count() == 1,
            "a union equal to an existing mask is interned immediately");
        Preserved(collision, Decode(collisionGrid));

        var sparse = Masks((0, [1]), (2, [2]), (4, [3]));
        var sparseGrid = Grid([0, 2, 4], 1);
        var sparseOutput = OcclusionComposer.RebuildGrid(GameId.UYA, sparseGrid, sparse);
        Check(sparseOutput.Length <= sparseGrid.Length && Decode(sparseOutput).Count == 3, "disconnected cells fit without inventing octants");
        Preserved(sparse, Decode(sparseOutput));

        var reserved = Masks((0, [1]), (1, [1, 2]), (2, [3, 1023]), (3, [3, 4, 1023]));
        var reservedOutput = Decode(OcclusionComposer.RebuildGrid(GameId.UYA, Grid([0, 1, 2, 3], 2), reserved));
        Preserved(reserved, reservedOutput);
        Throws<InvalidDataException>(() => OcclusionComposer.RebuildGrid(GameId.UYA, Grid([0, 1, 2, 3], 1), reserved));
        Throws<OperationCanceledException>(() => OcclusionComposer.RebuildGrid(GameId.UYA, source, masks, new(true)));
        Throws<OperationCanceledException>(() => OcclusionComposer.CompactGrid(GameId.UYA, source, raw, new(true)));
        Throws<InvalidDataException>(() => OcclusionComposer.RebuildGrid(GameId.UYA, source, Masks((0, [1]))));
        var invalid = masks.ToDictionary(p => p.Key, p => p.Value); invalid[(0, 0, 0)] = new byte[127];
        Throws<InvalidDataException>(() => OcclusionComposer.RebuildGrid(GameId.UYA, source, invalid));
        Throws<InvalidDataException>(() => OcclusionComposer.CompactGrid(GameId.UYA, source, sparseOutput));
        Throws<NotSupportedException>(() => OcclusionComposer.RebuildGrid(GameId.DL, source, masks));
        Throws<NotSupportedException>(() => OcclusionComposer.CompactGrid(GameId.DL, source, raw));
        Console.WriteLine("PASS allocation-bounded occlusion unions, weighting, interning, sparse cells, reserved bits and cache compatibility");
    }

    private static Dictionary<(int, int, int), byte[]> Masks(params (int X, int[] Bits)[] cells)
        => cells.ToDictionary(c => (c.X, 0, 0), c => Mask(c.Bits));

    private static byte[] Mask(params int[] bits)
    {
        var result = new byte[128];
        foreach (var bit in bits) result[bit / 8] |= (byte)(1 << (bit & 7));
        return result;
    }

    private static byte[] Grid(int[] xs, int slots)
    {
        var tree = (24 + (xs.Max() + 1) * 2 + 15) & ~15;
        var bytes = new byte[(tree + slots * 128 + 63) & ~63];
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), (ushort)value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes, tree);
        U16(6, 1); U16(8, 3); U16(14, 1); U16(16, 5); U16(22, xs.Max() + 1);
        for (var x = 0; x <= xs.Max(); x++) U16(24 + x * 2, xs.Contains(x) ? 0 : ushort.MaxValue);
        return bytes;
    }

    private static Dictionary<(int, int, int), byte[]> Decode(byte[] bytes)
    {
        var grid = UyaOcclusionGridReader.Read(bytes);
        return grid.Octants.ToDictionary(c => (c.X, c.Y, c.Z), c => bytes.AsSpan(grid.MasksOffset + c.MaskIndex * 128, 128).ToArray());
    }

    private static void Preserved(IReadOnlyDictionary<(int, int, int), byte[]> before, IReadOnlyDictionary<(int, int, int), byte[]> after)
    {
        Check(before.Count == after.Count && before.Keys.All(after.ContainsKey), "octant coordinates are unchanged");
        foreach (var (cell, mask) in before)
        {
            Check(Enumerable.Range(0, 128).All(i => (mask[i] & after[cell][i]) == mask[i]), "no generated visibility bit is lost");
            Check((mask[127] & 128) == (after[cell][127] & 128), "reserved visibility bit is unchanged");
        }
    }

    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
