using RatchetPs2.Core.Ties;

internal static class GcTieVuUploadAssertions
{
    public static void Validate(TiePacketDataBlock block)
    {
        var header = block.Bytes.AsSpan(0x20, 12);
        var vertices = block.DecodedVertices;
        var cursor = 0;
        // Fast VU 0b40/0fc0 leave three/two prefetched dinky vertices after their stop markers.
        Group(TiePacketDecodedVertexKind.Dinky, header[4], 3, shared: false);
        if (header[0] == 0) Group(TiePacketDecodedVertexKind.Dinky, header[5], 2, shared: true);
        if (cursor != header[10]) Fail("dinky loop reads past its upload group");
        // Fast VU 1ab0/1c40 stop on the last single/shared fat vertex.
        if (header[1] == 0)
        {
            Group(TiePacketDecodedVertexKind.Fat, header[6], 0, shared: false);
            Group(TiePacketDecodedVertexKind.Fat, header[7], 0, shared: true);
        }
        if (cursor != vertices.Count) Fail("fat loop reads past its upload group");

        void Group(TiePacketDecodedVertexKind kind, int stop, int tail, bool shared)
        {
            var found = false;
            while (!found)
            {
                var vertex = Next();
                found = vertex.GsPacketWriteOffset == stop;
            }
            for (var index = 0; index < tail; index++) Next();

            TiePacketDecodedVertex Next()
            {
                if (cursor >= vertices.Count) Fail("VU loop does not reach its stop marker");
                var vertex = vertices[cursor++];
                if (vertex.Kind != kind || (vertex.SecondaryGsPacketWriteOffset != 0) != shared)
                    Fail("VU loop consumes the wrong vertex kind or write mode");
                return vertex;
            }
        }

        void Fail(string message) => throw new Exception($"LOD {block.LodIndex} packet {block.PacketIndex}: {message}");
    }
}
