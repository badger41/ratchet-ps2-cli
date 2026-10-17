using System.Buffers.Binary;

namespace RatchetPs2.Games.UYA.Ties;

internal static partial class UyaStaticTieBinaryWriter
{
    private static byte[] BuildPacketBytes(PacketBuild packet)
    {
        var controlCount = checked(3 + packet.StripVertexCounts.Count);
        var controlSize = (controlCount + 3) / 4;
        packet.ControlCount = checked((byte)controlCount);
        packet.ControlSize = checked((byte)controlSize);
        packet.VertexOffset = checked((byte)(2 + controlSize));
        packet.VertexSize = checked((byte)packet.Vertices.Count);
        packet.ScissorOffset = checked((byte)(packet.VertexOffset + packet.VertexSize));
        packet.ScissorSize = checked((byte)((packet.Vertices.Count + 1 + 15) / 16));
        var bytes = new byte[(packet.ScissorOffset + packet.ScissorSize) * 0x10];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x0c), -9);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), packet.MaterialIndex * ShaderSize);
        bytes[0x20] = 1;
        bytes[0x20 + 1] = 1;
        bytes[0x20 + 2] = 6;
        bytes[0x20 + 3] = checked((byte)packet.StripVertexCounts.Count);
        bytes[0x20 + 4] = 7;
        bytes[0x20 + 8] = checked((byte)(4 + packet.Vertices.Count * 2));
        bytes[0x20 + 9] = 6;
        bytes[0x20 + 10] = checked((byte)packet.Vertices.Count);

        var vertexIndex = 0;
        var controlVuAddress = 6;
        for (var stripIndex = 0; stripIndex < packet.StripVertexCounts.Count; stripIndex++)
        {
            var stripVertexCount = packet.StripVertexCounts[stripIndex];
            var storageVertexStart = packet.Vertices.Count - vertexIndex - stripVertexCount;
            var controlOffset = 0x20 + 12 + stripIndex * 4;
            bytes[controlOffset] = checked((byte)stripVertexCount);
            bytes[controlOffset + 2] = checked((byte)controlVuAddress);
            bytes[controlOffset + 3] = 0x20;
            for (var stripVertexIndex = 0; stripVertexIndex < stripVertexCount; stripVertexIndex++, vertexIndex++)
            {
                var vertex = packet.Vertices[vertexIndex];
                var vuAddress = checked((ushort)(controlVuAddress + 1 + stripVertexIndex * 3));
                var storageVertexIndex = storageVertexStart + stripVertexIndex;
                var vertexOffset = (packet.VertexOffset + storageVertexIndex) * 0x10;
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertexOffset), vertex.X);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertexOffset + 2), vertex.Y);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertexOffset + 4), vertex.Z);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexOffset + 6), vuAddress);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexOffset + 8), vertex.S);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexOffset + 10), vertex.T);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexOffset + 12), 4096);
                bytes[packet.ScissorOffset * 0x10 + vertexIndex] = stripIndex == 0 && stripVertexIndex == 0
                    ? (byte)7
                    : stripVertexIndex == 0 ? (byte)0xfc : (byte)3;
            }
            controlVuAddress += 1 + stripVertexCount * 3;
        }
        bytes[packet.ScissorOffset * 0x10 + packet.Vertices.Count] = 0xF6;
        return bytes;
    }

    private static void WritePacketRecord(
        Span<byte> destination,
        PacketBuild packet,
        int tableOffset,
        int packetOffset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, checked((uint)(packetOffset - tableOffset)));
        destination[4] = 1;
        destination[5] = packet.BfcDistance;
        destination[6] = packet.ControlCount;
        destination[7] = packet.ControlSize;
        destination[8] = packet.VertexOffset;
        destination[9] = packet.VertexSize;
        destination[10] = packet.VertexSize;
        destination[12] = packet.ScissorOffset;
        destination[13] = packet.ScissorSize;
    }

    private static int GetStorageVertexIndex(IReadOnlyList<int> stripVertexCounts, int logicalVertexIndex)
    {
        var logicalStripStart = 0;
        var vertexCount = stripVertexCounts.Sum();
        foreach (var stripVertexCount in stripVertexCounts)
        {
            if (logicalVertexIndex < logicalStripStart + stripVertexCount)
                return vertexCount - logicalStripStart - stripVertexCount + logicalVertexIndex - logicalStripStart;
            logicalStripStart += stripVertexCount;
        }
        throw new ArgumentOutOfRangeException(nameof(logicalVertexIndex));
    }

}
