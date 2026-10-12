namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    internal static IReadOnlyList<TfragPositionPacket> FindNativePositionPackets(
        TfragTerrain terrain,
        TfragChunk chunk)
    {
        var result = new Dictionary<int, TfragPositionPacket>();
        for (var lodIndex = 0; lodIndex < 3; lodIndex++)
        {
            var setupPackets = new List<TfragPositionPacket>();
            var positionPackets = new List<TfragPositionPacket>();
            var vertexReferencePackets = new List<TfragPositionPacket>();
            var topologyPackets = new List<TfragTopologyPacket>();
            var state = TfragVifState.Default;
            var references = new TfragReferenceState();
            foreach (var segment in BuildRuntimeLodRecoveryLayout(chunk, lodIndex).ScanSegments)
                ScanSegment(
                    terrain.SourceBytes.Span,
                    chunk,
                    segment,
                    setupPackets,
                    positionPackets,
                    vertexReferencePackets,
                    topologyPackets,
                    ref state,
                    references);
            foreach (var packet in positionPackets) result.TryAdd(packet.Offset, packet);
        }
        return result.Values.OrderBy(packet => packet.Offset).ToArray();
    }
}
