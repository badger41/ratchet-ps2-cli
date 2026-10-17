using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Games.UYA.Ties;

internal static class UyaTriangleStripBuilder
{
    private const int MaximumStripsPerPacket = 15;
    private const int MaximumVuAddressExclusive = 0xcc;
    private const int MaximumVerticesPerPacket = (MaximumVuAddressExclusive - 7) / 3;
    // Keep ordinary models on the proven paired-loop shape. Near the packet/remap ceiling, one additional
    // triangle pair per strip avoids overflowing the runtime's signed packet count and terminated group table.
    private const int DefaultMaximumStripVertices = 4;
    public const int CompactMaximumStripVertices = 6;
    public const int MaximumSourceTriangles = UyaStaticTieBinaryWriter.MaximumPackets
        * MaximumStripsPerPacket * (CompactMaximumStripVertices - 2);

    public static IReadOnlyList<UyaTrianglePacket> Build(
        StaticModelPrimitive primitive,
        CancellationToken cancellationToken,
        int maximumStripVertices = DefaultMaximumStripVertices)
    {
        if (maximumStripVertices is < 4 or > CompactMaximumStripVertices || maximumStripVertices % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStripVertices));
        var triangleCount = primitive.Indices.Length / 3;
        var consumed = new bool[triangleCount];
        var edges = new TriangleDirectedEdgeIndex(primitive.Indices, cancellationToken);
        if (!edges.HasConnectionsBetweenTriangles())
            return Pack(BuildIsolatedStrips(primitive.Indices, cancellationToken));
        var builtStrips = new List<UyaTriangleStrip>();
        var remaining = triangleCount;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<UyaStripVertex>? strip = null;
            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (consumed[triangle]) continue;
                var candidate = BuildStrip(primitive.Indices, triangle, consumed, edges, maximumStripVertices);
                if (strip is null || candidate.Count > strip.Count)
                    strip = candidate;
                if (strip.Count == maximumStripVertices) break;
            }
            var selected = strip ?? throw new InvalidDataException("UYA TIE strip construction left source triangles unhandled.");
            var sourceTriangles = selected.Select(value => value.SourceTriangleIndex).Distinct().ToArray();
            foreach (var sourceTriangle in sourceTriangles)
                consumed[sourceTriangle] = true;
            remaining -= sourceTriangles.Length;
            // The UYA VU loop consumes strip vertices in pairs; retail pads odd strips with a degenerate tail.
            if (selected.Count % 2 != 0)
                selected = selected.Append(selected[^1]).ToArray();
            builtStrips.Add(new(selected));
        }

        return Pack(builtStrips);
    }

    private static IReadOnlyList<UyaTrianglePacket> Pack(IReadOnlyList<UyaTriangleStrip> strips)
    {
        var ordered = strips.OrderByDescending(value => value.Vertices.Count).ToArray();
        var minimumPacketCount = Math.Max(
            (ordered.Length + MaximumStripsPerPacket - 1) / MaximumStripsPerPacket,
            (ordered.Sum(value => value.Vertices.Count) + MaximumVerticesPerPacket - 1) / MaximumVerticesPerPacket);
        PacketAccumulator[]? packets = null;
        for (var packetCount = minimumPacketCount; packetCount <= ordered.Length && packets is null; packetCount++)
        {
            var candidate = Enumerable.Range(0, packetCount).Select(_ => new PacketAccumulator()).ToArray();
            var packed = true;
            foreach (var strip in ordered)
            {
                PacketAccumulator? packet = null;
                foreach (var value in candidate)
                {
                    if (!CanPack(value.Strips.Count, value.VertexCount, strip.Vertices.Count)) continue;
                    if (packet is null
                        || value.Strips.Count < packet.Strips.Count
                        || value.Strips.Count == packet.Strips.Count && value.Score < packet.Score)
                        packet = value;
                }
                if (packet is null)
                {
                    packed = false;
                    break;
                }
                packet.Add(strip);
            }
            if (packed) packets = candidate;
        }
        return packets!.Select(packet => new UyaTrianglePacket(
            packet.Strips.OrderBy(value => value.Vertices.Count).ToArray())).ToArray();
    }

    public static IReadOnlyList<UyaTrianglePacket> RepackForRemapGroups(
        IReadOnlyList<UyaTrianglePacket> packets,
        UyaRemapGroupPackingState groupPacking)
    {
        var strips = packets.SelectMany(packet => packet.Strips).ToArray();
        var repacked = new List<UyaTrianglePacket>();
        var stripIndex = 0;
        while (stripIndex < strips.Length)
        {
            var packet = new List<UyaTriangleStrip>();
            var packetVertexCount = 0;
            while (stripIndex < strips.Length
                && CanPack(packet.Count, packetVertexCount, strips[stripIndex].Vertices.Count))
            {
                var candidateVertexCount = packetVertexCount + strips[stripIndex].Vertices.Count;
                if (!groupPacking.CanAppendPacket(candidateVertexCount)) break;
                var strip = strips[stripIndex++];
                packet.Add(strip);
                packetVertexCount += strip.Vertices.Count;
            }
            if (packet.Count == 0)
            {
                if (!groupPacking.HasPackets)
                    throw new InvalidDataException("A UYA TIE strip is too large for one normal/RGBA remap group.");
                groupPacking.Reset();
                continue;
            }
            groupPacking.AppendPacket(packetVertexCount);
            repacked.Add(new(packet));
        }
        return repacked;
    }

    private static bool CanPack(int stripCount, int vertexCount, int addedVertexCount)
    {
        if (stripCount == MaximumStripsPerPacket) return false;
        vertexCount += addedVertexCount;
        if (vertexCount > MaximumVerticesPerPacket) return false;
        stripCount++;
        // Strip controls carry byte-sized VU addresses; keep the whole generated packet in that address space.
        return 6 + stripCount + vertexCount * 3 <= MaximumVuAddressExclusive;
    }

    private static IReadOnlyList<UyaTriangleStrip> BuildIsolatedStrips(
        uint[] indices,
        CancellationToken cancellationToken)
    {
        var strips = new UyaTriangleStrip[indices.Length / 3];
        for (var triangle = 0; triangle < strips.Length; triangle++)
        {
            if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = triangle * 3;
            strips[triangle] = new(new UyaStripVertex[]
            {
                new(indices[offset], triangle),
                new(indices[offset + 2], triangle),
                new(indices[offset + 1], triangle),
                new(indices[offset + 1], triangle),
            });
        }
        return strips;
    }

    private static IReadOnlyList<UyaStripVertex> BuildStrip(
        uint[] indices,
        int triangle,
        bool[] consumed,
        TriangleDirectedEdgeIndex edges,
        int maximumStripVertices)
    {
        var offset = triangle * 3;
        UyaStripVertex[]? longest = null;
        Span<UyaStripVertex> vertices = stackalloc UyaStripVertex[CompactMaximumStripVertices];
        for (var rotation = 0; rotation < 3; rotation++)
        {
            var a = indices[offset + rotation];
            var b = indices[offset + (rotation + 1) % 3];
            var c = indices[offset + (rotation + 2) % 3];
            vertices[0] = new(a, triangle);
            vertices[1] = new(c, triangle);
            vertices[2] = new(b, triangle);
            var vertexCount = 3;
            while (vertexCount < maximumStripVertices)
            {
                var left = vertices[vertexCount - 2].SourceVertexIndex;
                var right = vertices[vertexCount - 1].SourceVertexIndex;
                var edge = vertexCount % 2 == 0 ? (right, left) : (left, right);
                TriangleEdgeContinuation? continuation = null;
                foreach (var value in edges.Get(edge.Item1, edge.Item2))
                {
                    if (consumed[value.TriangleIndex]
                        || ContainsTriangle(vertices, vertexCount, value.TriangleIndex)) continue;
                    continuation = value;
                    break;
                }
                if (continuation is not { } next) break;
                vertices[vertexCount++] = new(next.NextVertex, next.TriangleIndex);
            }
            if (longest is null || vertexCount > longest.Length)
                longest = vertices[..vertexCount].ToArray();
        }

        return longest!;
    }

    private static bool ContainsTriangle(ReadOnlySpan<UyaStripVertex> vertices, int count, int triangle)
    {
        for (var index = 0; index < count; index++)
        {
            if (vertices[index].SourceTriangleIndex == triangle) return true;
        }
        return false;
    }

}

internal sealed class PacketAccumulator
{
    public List<UyaTriangleStrip> Strips { get; } = [];
    public int VertexCount { get; private set; }
    public int LargestStripVertexCount { get; private set; }
    public int Score => VertexCount - LargestStripVertexCount;

    public void Add(UyaTriangleStrip strip)
    {
        Strips.Add(strip);
        VertexCount += strip.Vertices.Count;
        LargestStripVertexCount = Math.Max(LargestStripVertexCount, strip.Vertices.Count);
    }
}

internal sealed record UyaTrianglePacket(IReadOnlyList<UyaTriangleStrip> Strips);

internal sealed record UyaTriangleStrip(IReadOnlyList<UyaStripVertex> Vertices);

internal readonly record struct UyaStripVertex(uint SourceVertexIndex, int SourceTriangleIndex);
