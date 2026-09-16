using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TiePacketDataBlockReader
{
    private const int PacketControlStartQword = 2;

    public static List<TiePacketDataBlock> Read(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketTable> tables)
    {
        var packets = new List<TiePacket>();
        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            for (var packetIndex = 0; packetIndex < table.Packets.Count; packetIndex++)
            {
                var packet = table.Packets[packetIndex];
                if (packet.DataOffset > 0)
                {
                    packets.Add(packet);
                }
            }
        }
        packets.Sort(static (left, right) => left.AbsoluteDataOffset.CompareTo(right.AbsoluteDataOffset));

        var blocks = new List<TiePacketDataBlock>(packets.Count);
        for (var packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            var packet = packets[packetIndex];
            var offset = packet.AbsoluteDataOffset;
            var qwordCount = GetPacketQwordCount(packet);
            var length = qwordCount * 0x10;
            EnsureRange(bytes, offset, length, $"packet data LOD{packet.LodIndex}[{packet.PacketIndex}]");
            var blockBytes = Slice(bytes, offset, length);
            var controlRows = TiePacketControlDecoder.DecodeControlRows(bytes, packet);
            var unpackHeader = TiePacketControlDecoder.DecodeUnpackHeader(controlRows);
            var stripControls = TiePacketControlDecoder.DecodeStripControls(bytes, packet, controlRows);
            var setupRows = TiePacketControlDecoder.DecodeSetupRows(bytes, packet);
            var vertexRows = TiePacketVertexDecoder.DecodeVertexRows(bytes, header, packet, unpackHeader);
            var decodedVertices = TiePacketVertexDecoder.DecodePacketVertices(bytes, packet, unpackHeader, vertexRows);
            var physicalPrimitives = TiePacketPrimitiveDecoder.DecodePacketPrimitives(
                setupRows,
                stripControls,
                decodedVertices,
                useStripTokenReferences: false);
            var tokenReferencePrimitives = TiePacketPrimitiveDecoder.DecodePacketPrimitives(
                setupRows,
                stripControls,
                decodedVertices,
                useStripTokenReferences: true);
            blocks.Add(new TiePacketDataBlock
            {
                LodIndex = packet.LodIndex,
                PacketIndex = packet.PacketIndex,
                Offset = offset,
                Length = length,
                QwordCount = qwordCount,
                Bytes = blockBytes,
                Regions = BuildPacketDataRegions(bytes, packet, qwordCount),
                SetupRows = setupRows,
                UnpackHeader = unpackHeader,
                ControlRows = controlRows,
                StripControls = stripControls,
                StripTokens = CollectStripTokens(stripControls),
                ScissorTokens = TiePacketControlDecoder.DecodeScissorTokens(bytes, packet, stripControls),
                VertexRows = vertexRows,
                DecodedVertices = decodedVertices,
                PhysicalPrimitives = physicalPrimitives,
                TokenReferencePrimitives = tokenReferencePrimitives,
                Primitives = physicalPrimitives
            });
        }

        return blocks;
    }

    private static TiePacketStripToken[] CollectStripTokens(IReadOnlyList<TiePacketStripControl> stripControls)
    {
        var count = 0;
        for (var i = 0; i < stripControls.Count; i++)
        {
            count += stripControls[i].DecodedTokens.Count;
        }

        var tokens = GC.AllocateUninitializedArray<TiePacketStripToken>(count);
        var tokenIndex = 0;
        for (var stripIndex = 0; stripIndex < stripControls.Count; stripIndex++)
        {
            var stripTokens = stripControls[stripIndex].DecodedTokens;
            for (var i = 0; i < stripTokens.Count; i++)
            {
                tokens[tokenIndex++] = stripTokens[i];
            }
        }
        return tokens;
    }

    public static int GetPacketQwordCount(TiePacket packet)
    {
        var qwords = 0;
        Consider(packet.VertexOffset, packet.VertexSize);
        Consider(packet.ScissorOffset, packet.ScissorSize);

        if (packet.MultipassOffset > 0 && packet.MultipassUvSize > 0)
        {
            Consider(
                packet.MultipassOffset,
                TiePassFlags.GeneratedEnvPassHeaderQwords + packet.MultipassUvSize);
        }

        return qwords;

        void Consider(byte offset, int count)
        {
            if (count == 0)
            {
                return;
            }

            qwords = Math.Max(qwords, offset + count);
        }
    }

    private static List<TiePacketDataRegion> BuildPacketDataRegions(
        byte[] bytes,
        TiePacket packet,
        int qwordCount)
    {
        var regions = new List<TiePacketDataRegion>();
        AddRegion("setup-rows", 0, TiePacketControlDecoder.PacketSetupQwordCount);
        AddRegion("control-region", PacketControlStartQword, packet.VertexOffset - PacketControlStartQword);
        AddRegion("vertex-rows", packet.VertexOffset, packet.VertexSize);
        AddRegion("scissor-rows", packet.ScissorOffset, packet.ScissorSize);

        if (packet.MultipassOffset > 0 && packet.MultipassUvSize > 0)
        {
            AddRegion(
                "multipass-uv",
                packet.MultipassOffset,
                TiePassFlags.GeneratedEnvPassHeaderQwords + packet.MultipassUvSize);
        }

        regions.Sort(static (left, right) =>
        {
            var comparison = left.QwordOffset.CompareTo(right.QwordOffset);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });
        return regions;

        void AddRegion(string name, int qwordOffset, int regionQwordCount)
        {
            if (regionQwordCount <= 0 || qwordOffset >= qwordCount)
            {
                return;
            }

            var clampedQwordCount = Math.Min(regionQwordCount, qwordCount - qwordOffset);
            var offset = packet.AbsoluteDataOffset + qwordOffset * 0x10;
            var length = clampedQwordCount * 0x10;
            regions.Add(new TiePacketDataRegion
            {
                Name = name,
                QwordOffset = qwordOffset,
                QwordCount = clampedQwordCount,
                Offset = offset,
                Length = length,
                Bytes = Slice(bytes, offset, length)
            });
        }
    }
}
