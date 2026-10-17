using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static List<byte> BuildZeroMarkerTopologyTokens(IReadOnlyList<byte> sourceTokens, byte[] prefixBytes)
    {
        if (sourceTokens.Count == 0)
        {
            return [];
        }

        var first = unchecked((sbyte)sourceTokens[0]);
        prefixBytes[2] = checked((byte)(first + 0x80));

        var tokens = new List<byte>(sourceTokens.Count + 4) { 0 };
        for (var i = 1; i < sourceTokens.Count; i++)
        {
            tokens.Add(sourceTokens[i]);
        }

        var trailerStart = Math.Max(0, sourceTokens.Count - 3);
        for (var i = trailerStart; i < sourceTokens.Count; i++)
        {
            tokens.Add(sourceTokens[i]);
        }

        tokens.Add(0);
        return tokens;
    }

    private static bool TemplateUsesZeroMarkerTopology(
        Ps2VifPacketSpan? topologyPacket,
        byte[] combinedVifData,
        byte[]? textureData)
    {
        if (topologyPacket is null || topologyPacket.PayloadLength < 8)
        {
            return false;
        }

        var payloadOffset = topologyPacket.Offset + 4;
        var payloadLength = Math.Min(topologyPacket.PayloadLength, combinedVifData.Length - payloadOffset);
        if (payloadLength < 8)
        {
            return false;
        }

        var payload = combinedVifData.AsSpan(payloadOffset, payloadLength);
        if (payload[4..].IndexOf((byte)0) >= 0)
        {
            return true;
        }

        return textureData is not null
            && Ps2VifPacket.ReadSpans(textureData).Any(packet => packet.IsUnpack && (packet.Command & 0x0F) == 0x0C);
    }

    private static byte[] BuildStandaloneTopologyVifData(byte[] payload)
    {
        var num = checked((byte)(payload.Length / 4));
        using var stream = new MemoryStream();
        stream.Write(Ps2VifPacket.BuildBytes(immediate: 0, num, commandByte: 0x6E, payload)); // UNPACK_V4_8
        using var writer = new BinaryWriter(stream);
        Align(writer, 0x10);
        return stream.ToArray();
    }

    private static byte[] BuildGeneratedMinimalTopologyVifData(
        byte[] payload,
        byte vertexDomainCount,
        bool compactTopologyPacket,
        bool hasTextureMetadata)
    {
        const ushort generatedTopologyImmediate = 0x812D;
        const int guardPaddingBytes = 12;
        const int generatedTextureOverlapBytes = 8;

        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = compactTopologyPacket && hasTextureMetadata
            ? generatedTextureOverlapBytes
            : 0;
        var targetPayloadLength = Align(payload.Length + guardPaddingBytes + textureOverlapBytes, 4);
        while (compactTopologyPacket
            && (topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        if (targetPayloadLength / 4 > 0xFF)
        {
            throw new InvalidDataException(
                $"Regenerated topology VIF payload is {targetPayloadLength} bytes. v1 importer supports at most 1020 bytes per topology packet.");
        }

        var replacementPayload = new byte[targetPayloadLength];
        Array.Fill<byte>(replacementPayload, 0x80);
        payload.CopyTo(replacementPayload, 0);

        using var stream = new MemoryStream();
        WriteGeneratedVertexDomainUnpack(stream, vertexDomainCount);
        stream.Write(Ps2VifPacket.BuildBytes(
            generatedTopologyImmediate,
            checked((byte)(targetPayloadLength / 4)),
            commandByte: 0x6E,
            replacementPayload)); // UNPACK_V4_8
        if (textureOverlapBytes > 0)
        {
            stream.SetLength(stream.Length - textureOverlapBytes);
        }

        using var writer = new BinaryWriter(stream);
        Align(writer, 0x10);
        return stream.ToArray();
    }

    private static short Quantize(float value, ref int clipCount)
    {
        var rounded = MathF.Round(value);
        if (rounded < short.MinValue)
        {
            clipCount++;
            return short.MinValue;
        }

        if (rounded > short.MaxValue)
        {
            clipCount++;
            return short.MaxValue;
        }

        return checked((short)rounded);
    }


    private static int EstimateGeneratedTopologyTokenCount(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry)
    {
        return BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology: false).Count;
    }

    private static int EstimateIsolatedTriangleTopologyTokenCount(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry)
    {
        return BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology: true).Count;
    }

    private static bool GeneratedTopologyFitsCompactPacketBudget(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        int vertexDomainCount,
        bool isolatedTriangleTopology)
    {
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null)
        {
            return true;
        }

        const int topologyPrefixBytes = 4;
        const int guardPaddingBytes = 12;
        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = ResolveTemplateTextureOverlapBytes(
            packet,
            templateEntry.VifData.Length,
            templateEntry.VifTextureData);
        var tokenCount = BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology).Count;
        var targetPayloadLength = Align(topologyPrefixBytes + tokenCount + guardPaddingBytes + textureOverlapBytes, 4);
        while ((topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        const int safetyMarginBytes = 0x10;
        return targetPayloadLength + safetyMarginBytes <= packet.PayloadLength;
    }

    private static bool GeneratedTopologyFitsGeneratedMinimalPacketBudget(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        int vertexDomainCount,
        bool isolatedTriangleTopology,
        bool compactTopologyPacket)
    {
        const int topologyPrefixBytes = 4;
        const int guardPaddingBytes = 12;
        const int generatedTextureOverlapBytes = 8;
        const int maxPacketPayloadBytes = 0xFF * 4;

        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = compactTopologyPacket && templateEntry.VifTextureData is { Length: > 0 }
            ? generatedTextureOverlapBytes
            : 0;
        var tokenCount = BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology).Count;
        var targetPayloadLength = Align(topologyPrefixBytes + tokenCount + guardPaddingBytes + textureOverlapBytes, 4);
        while (compactTopologyPacket
            && (topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        return targetPayloadLength <= maxPacketPayloadBytes;
    }

    private static List<byte> BuildEstimatedTopologyTokens(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        bool isolatedTriangleTopology)
    {
        var topologyTokens = isolatedTriangleTopology
            ? Ps2VifTopology.BuildIsolatedTriangleTokens(triangleIndices)
            : Ps2VifTopology.BuildRestartStripTokens(triangleIndices);
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (TemplateUsesZeroMarkerTopology(packet, combinedVifData, templateEntry.VifTextureData))
        {
            var prefixBytes = packet is not null && packet.PayloadLength >= 4
                ? combinedVifData.AsSpan(packet.Offset + 4, 4).ToArray()
                : [0, 0, 0x80, 0];
            topologyTokens = BuildZeroMarkerTopologyTokens(topologyTokens, prefixBytes);
        }

        return topologyTokens;
    }
}
