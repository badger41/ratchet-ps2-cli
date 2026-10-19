using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Geometry;

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
        return Pack(UyaTriangleStripPlanner.Build(primitive.Indices, cancellationToken, maximumStripVertices));
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
