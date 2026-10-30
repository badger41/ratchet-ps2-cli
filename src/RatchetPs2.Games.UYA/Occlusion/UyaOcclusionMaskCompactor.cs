using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Occlusion;

namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Fits generated visibility into the source grid allocation using conservative mask unions.</summary>
internal static class UyaOcclusionMaskCompactor
{
    private const int MaskSize = 128;
    private const int MaximumQueuedEdges = 1_000_000;
    private const int MaximumCandidateWork = 20_000_000;

    internal static byte[] Rebuild(ReadOnlySpan<byte> original,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(masks);
        var grid = UyaOcclusionGridReader.Read(original, OcclusionSamplePlanner.MaximumCells, token);
        if (grid.Octants.Count == 0 || masks.Count != grid.Octants.Count
            || grid.Octants.Any(c => !masks.TryGetValue((c.X, c.Y, c.Z), out var mask) || mask is null || mask.Length != MaskSize))
            throw new InvalidDataException("Supply exactly one 128-byte mask per existing octant.");

        // Rebuilding aliased tree rows can change their size; measure the actual writer layout.
        var empty = new byte[MaskSize];
        var layout = UyaOcclusionGridWriter.ReplaceMasks(original,
            grid.Octants.ToDictionary(c => (c.X, c.Y, c.Z), _ => empty), token);
        var treeBytes = BinaryPrimitives.ReadInt32LittleEndian(layout);
        var maximumMasks = Math.Min(ushort.MaxValue, ((original.Length & ~63) - treeBytes) / MaskSize);
        if (maximumMasks < 1)
            throw new InvalidDataException("The source occlusion allocation cannot fit the rebuilt lookup tree and a visibility mask.");

        var nodes = new List<Node>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var cells = new Dictionary<(int X, int Y, int Z), int>();
        foreach (var c in grid.Octants)
        {
            token.ThrowIfCancellationRequested();
            var coord = (c.X, c.Y, c.Z);
            var mask = masks[coord];
            var key = Convert.ToHexString(mask);
            if (!ids.TryGetValue(key, out var id))
            {
                id = nodes.Count;
                ids.Add(key, id);
                nodes.Add(new Node(mask.ToArray(), key));
            }
            nodes[id].Cells.Add(coord);
            cells.Add(coord, id);
        }

        var active = nodes.Count;
        if (active > maximumMasks)
        {
            foreach (var (coord, id) in cells)
            {
                token.ThrowIfCancellationRequested();
                Connect(id, (coord.X + 1, coord.Y, coord.Z));
                Connect(id, (coord.X, coord.Y + 1, coord.Z));
                Connect(id, (coord.X, coord.Y, coord.Z + 1));
            }
            var queue = new PriorityQueue<Edge, (long Cost, int Left, int Right)>();
            var candidateWork = 0;
            RebuildQueue();
            while (active > maximumMasks)
            {
                token.ThrowIfCancellationRequested();
                if (!queue.TryDequeue(out var edge, out _))
                {
                    // Sparse/disconnected islands still need a bounded route to the allocation budget.
                    foreach (var domain in Enumerable.Range(0, nodes.Count).Where(i => nodes[i].Cells.Count != 0)
                        .GroupBy(i => nodes[i].ReservedVisible))
                    {
                        var order = domain.OrderBy(i => nodes[i].Key, StringComparer.Ordinal).ToArray();
                        for (var i = 1; i < order.Length; i++)
                        {
                            var a = order[i - 1]; var b = order[i];
                            nodes[a].Neighbors.Add(b); nodes[b].Neighbors.Add(a);
                            Enqueue(a, b);
                        }
                    }
                    if (queue.Count == 0)
                        throw new InvalidDataException("Occlusion cannot fit its source allocation while preserving reserved visibility bits.");
                    continue;
                }
                if (nodes[edge.Left].Cells.Count == 0 || nodes[edge.Right].Cells.Count == 0
                    || nodes[edge.Left].Version != edge.LeftVersion || nodes[edge.Right].Version != edge.RightVersion)
                    continue;
                var root = Join(edge.Left, edge.Right);
                if (queue.Count > MaximumQueuedEdges) RebuildQueue();
                else foreach (var neighbor in nodes[root].Neighbors) Enqueue(root, neighbor);
            }

            void Enqueue(int left, int right)
            {
                token.ThrowIfCancellationRequested();
                if (++candidateWork > MaximumCandidateWork)
                    throw new InvalidDataException("Occlusion mask compaction exceeds its bounded candidate-work limit.");
                if (left > right) (left, right) = (right, left);
                var a = nodes[left]; var b = nodes[right];
                var cost = (long)a.Cells.Count * AddedBits(a.Mask, b.Mask)
                    + (long)b.Cells.Count * AddedBits(b.Mask, a.Mask);
                queue.Enqueue(new(left, right, a.Version, b.Version), (cost, left, right));
            }

            void RebuildQueue()
            {
                queue.Clear();
                for (var i = 0; i < nodes.Count; i++)
                    foreach (var neighbor in nodes[i].Neighbors)
                        if (i < neighbor) Enqueue(i, neighbor);
            }

            int Join(int left, int right)
            {
                // Joining a union already present elsewhere costs no additional visibility.
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (nodes[left].Cells.Count < nodes[right].Cells.Count
                        || nodes[left].Cells.Count == nodes[right].Cells.Count && left > right)
                        (left, right) = (right, left);
                    var a = nodes[left]; var b = nodes[right];
                    if (ids.GetValueOrDefault(a.Key, -1) == left) ids.Remove(a.Key);
                    if (ids.GetValueOrDefault(b.Key, -1) == right) ids.Remove(b.Key);
                    for (var i = 0; i < MaskSize; i++) a.Mask[i] |= b.Mask[i];
                    a.Key = Convert.ToHexString(a.Mask);
                    var collision = ids.GetValueOrDefault(a.Key, -1);
                    a.Neighbors.UnionWith(b.Neighbors);
                    a.Neighbors.Remove(left); a.Neighbors.Remove(right);
                    foreach (var neighbor in a.Neighbors)
                    {
                        token.ThrowIfCancellationRequested();
                        nodes[neighbor].Neighbors.Remove(right);
                        nodes[neighbor].Neighbors.Add(left);
                    }
                    b.Neighbors.Clear();
                    a.Cells.AddRange(b.Cells); b.Cells.Clear();
                    a.Version++; b.Version++;
                    active--; ids[a.Key] = left;
                    if (collision < 0) return left;
                    right = collision;
                }
            }
        }

        var outputMasks = new Dictionary<(int X, int Y, int Z), byte[]>(cells.Count);
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            foreach (var coord in node.Cells)
            {
                if (AddedBits(node.Mask, masks[coord]) != 0 || (node.Mask[127] & 128) != (masks[coord][127] & 128))
                    throw new InvalidDataException("Compaction removed generated visibility or changed a reserved bit.");
                outputMasks.Add(coord, node.Mask);
            }
        }
        var result = UyaOcclusionGridWriter.ReplaceMasks(original, outputMasks, token);
        if (result.Length > original.Length)
            throw new InvalidDataException("Compacted occlusion exceeds the source grid allocation.");
        return result;

        void Connect(int id, (int X, int Y, int Z) coord)
        {
            if (!cells.TryGetValue(coord, out var other) || id == other || nodes[id].ReservedVisible != nodes[other].ReservedVisible)
                return;
            nodes[id].Neighbors.Add(other); nodes[other].Neighbors.Add(id);
        }
    }

    private static int AddedBits(byte[] before, byte[] after)
    {
        var count = 0;
        for (var i = 0; i < MaskSize; i += 8)
            count += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(after.AsSpan(i))
                & ~BinaryPrimitives.ReadUInt64LittleEndian(before.AsSpan(i)));
        return count;
    }

    private sealed class Node(byte[] mask, string key)
    {
        public byte[] Mask { get; } = mask;
        public string Key { get; set; } = key;
        public bool ReservedVisible => (Mask[127] & 128) != 0;
        public List<(int X, int Y, int Z)> Cells { get; } = [];
        public HashSet<int> Neighbors { get; } = [];
        public int Version { get; set; }
    }

    private readonly record struct Edge(int Left, int Right, int LeftVersion, int RightVersion);
}
