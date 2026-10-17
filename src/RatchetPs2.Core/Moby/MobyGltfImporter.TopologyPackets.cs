using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static VifBuildResult BuildVifData(
        byte[] templateVifData,
        byte[]? templateVifTextureData,
        IReadOnlyList<uint> triangleIndices,
        bool preserveTemplateTopologyTail = false,
        bool compactTopologyPacket = false,
        bool forceZeroMarkerTopology = false,
        bool forceIsolatedTriangleTopology = false,
        bool generateMinimalVifContainer = false,
        byte? generatedVifDomainCapacity = null)
    {
        if (triangleIndices.Count < 3 || triangleIndices.Count % 3 != 0)
        {
            throw new InvalidDataException("glTF mesh indices must contain triangles.");
        }

        var adjustedTemplateVifTextureData = templateVifTextureData is null
            ? null
            : (byte[])templateVifTextureData.Clone();
        var templateCombinedVifData = Combine(templateVifData, adjustedTemplateVifTextureData);
        var topologyTokens = forceIsolatedTriangleTopology
            ? Ps2VifTopology.BuildIsolatedTriangleTokens(triangleIndices)
            : Ps2VifTopology.BuildRestartStripTokens(triangleIndices);
        var packet = TryFindTopologyPacket(templateCombinedVifData);
        var prefixBytes = packet is not null && packet.PayloadLength >= 4
            ? templateCombinedVifData.AsSpan(packet.Offset + 4, 4).ToArray()
            : [0xFE, 0x1F, 0x01, 0x00];
        var useZeroMarkerTopology = forceZeroMarkerTopology
            || TemplateUsesZeroMarkerTopology(packet, templateCombinedVifData, templateVifTextureData);
        if (useZeroMarkerTopology)
        {
            topologyTokens = BuildZeroMarkerTopologyTokens(topologyTokens, prefixBytes);
        }
        var payload = Ps2VifTopology.BuildPayload(topologyTokens, prefixBytes);
        var generatedRowUsage = SummarizeGeneratedTopologyRowUsage(
            payload,
            adjustedTemplateVifTextureData,
            Math.Max(127, GetMaxIndex(triangleIndices) + 1));
        if (packet is null)
        {
            var standaloneVertexDomainCount = generatedVifDomainCapacity
                ?? (byte)Math.Min(127, Math.Max(1, GetMaxIndex(triangleIndices) + 1));
            return new VifBuildResult(
                generateMinimalVifContainer
                    ? BuildGeneratedMinimalTopologyVifData(
                        payload,
                        standaloneVertexDomainCount,
                        compactTopologyPacket,
                        adjustedTemplateVifTextureData is not null)
                    : BuildStandaloneTopologyVifData(payload),
                adjustedTemplateVifTextureData,
                topologyTokens.Count - (triangleIndices.Count / 3),
                PreservedTemplateLayout: false,
                ExpandedTopologyPacket: false,
                OriginalTopologyPayloadBytes: 0,
                NewTopologyPayloadBytes: payload.Length,
                ReusedTemplateTopology: false,
                RemappedTemplateTopology: false,
                UsedMetadataTopologyLayout: false,
                GeneratedTopologyFromGltf: true,
                GeneratedTopologyTokenCount: topologyTokens.Count,
                GeneratedTopologySourceTriangleCount: triangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata: true,
                GeneratedTopologyMatchesSourceTriangles: true,
                GeneratedTopologyPreservesTemplateControlMarkers: true,
                GeneratedTopologyMatchesTemplateControlShape: true,
                TemplateTopologyRestartCount: 0,
                GeneratedTopologyRestartCount: 0,
                TemplateTopologyNegativeTokenCount: 0,
                GeneratedTopologyNegativeTokenCount: 0,
                TemplateTopologyShape: null,
                GeneratedTopologyShape: null,
                GeneratedTopologyRowUsage: generatedRowUsage);
        }

        var vertexDomainCount = generatedVifDomainCapacity
            ?? (TryReadLeadingVertexDomainUnpackCount(templateVifData, out var domainCount)
                ? domainCount
                : (byte)Math.Min(127, Math.Max(1, GetMaxIndex(triangleIndices) + 1)));
        var topologyPacketOffset = generateMinimalVifContainer
            ? 4 + vertexDomainCount * 4
            : packet.Offset;
        var compactLayout = compactTopologyPacket
            ? ResolveCompactTopologyLayout(payload.Length, topologyPacketOffset, packet, templateVifData.Length, adjustedTemplateVifTextureData)
            : null;
        var targetPayloadLength = compactLayout?.PayloadLength ?? Math.Max(payload.Length, packet.PayloadLength);
        targetPayloadLength = Align(targetPayloadLength, 4);
        if (targetPayloadLength / 4 > 0xFF)
        {
            throw new InvalidDataException(
                $"Regenerated topology VIF payload is {targetPayloadLength} bytes. v1 importer supports at most 1020 bytes per topology packet.");
        }

        var replacementPayload = new byte[targetPayloadLength];
        Array.Fill<byte>(replacementPayload, 0x80);
        if (preserveTemplateTopologyTail)
        {
            var templatePayloadLength = Math.Min(packet.PayloadLength, templateCombinedVifData.Length - packet.Offset - 4);
            if (templatePayloadLength > 0)
            {
                var copyLength = Math.Min(templatePayloadLength, replacementPayload.Length);
                templateCombinedVifData.AsSpan(packet.Offset + 4, copyLength)
                    .CopyTo(replacementPayload.AsSpan(0, copyLength));
            }
        }
        payload.CopyTo(replacementPayload, 0);

        using var stream = generateMinimalVifContainer
            ? new MemoryStream(4 + vertexDomainCount * 4 + 4 + targetPayloadLength)
            : new MemoryStream(templateCombinedVifData.Length - packet.TotalLength + 4 + targetPayloadLength);
        if (generateMinimalVifContainer)
        {
            WriteGeneratedVertexDomainUnpack(stream, vertexDomainCount);
        }
        else
        {
            stream.Write(templateCombinedVifData, 0, packet.Offset);
        }
        stream.Write(Ps2VifPacket.BuildFromTemplate(
            packet,
            checked((byte)(targetPayloadLength / 4)),
            replacementPayload));

        if (!compactTopologyPacket && !generateMinimalVifContainer)
        {
            var suffixOffset = packet.Offset + packet.TotalLength;
            stream.Write(templateCombinedVifData, suffixOffset, templateCombinedVifData.Length - suffixOffset);
        }
        var combinedResult = stream.ToArray();
        Array.Resize(ref combinedResult, Align(combinedResult.Length, 0x10));
        byte[] resultVifData;
        byte[]? resultVifTextureData;
        if (compactTopologyPacket)
        {
            var compactVifDataLength = topologyPacketOffset + 4 + targetPayloadLength - (compactLayout?.TextureOverlapBytes ?? 0);
            if (compactVifDataLength <= 0 || compactVifDataLength % 0x10 != 0)
            {
                compactVifDataLength = Align(packet.Offset + 4 + targetPayloadLength, 0x10);
            }

            resultVifData = new byte[compactVifDataLength];
            Array.Copy(combinedResult, resultVifData, Math.Min(compactVifDataLength, combinedResult.Length));
            resultVifTextureData = adjustedTemplateVifTextureData;
        }
        else
        {
            SplitCombinedVifData(combinedResult, templateVifData.Length, out resultVifData, out resultVifTextureData);
        }

        return new VifBuildResult(
            resultVifData,
            resultVifTextureData,
            topologyTokens.Count - (triangleIndices.Count / 3),
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: targetPayloadLength > packet.PayloadLength,
            OriginalTopologyPayloadBytes: packet.PayloadLength,
            NewTopologyPayloadBytes: payload.Length,
            ReusedTemplateTopology: false,
            RemappedTemplateTopology: false,
            UsedMetadataTopologyLayout: false,
            GeneratedTopologyFromGltf: true,
            GeneratedTopologyTokenCount: topologyTokens.Count,
            GeneratedTopologySourceTriangleCount: triangleIndices.Count / 3,
            GeneratedTopologyPayloadFitsMetadata: targetPayloadLength <= packet.PayloadLength,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null,
            TemplateTopologyTrace: null,
            GeneratedTopologyTrace: null,
            GeneratedTopologyRowUsage: generatedRowUsage,
            TemplateTopologyZeroMarkers: null,
            GeneratedTopologyZeroMarkers: null,
            CompactTopologyTextureOverlapBytes: compactLayout?.TextureOverlapBytes);
    }

    private static void WriteGeneratedVertexDomainUnpack(Stream stream, byte vertexDomainCount)
    {
        const ushort vertexDomainImmediate = 0x80C2;
        const byte vertexDomainCommand = 0x75;
        stream.Write(Ps2VifPacket.BuildBytes(
            vertexDomainImmediate,
            vertexDomainCount,
            vertexDomainCommand,
            new byte[vertexDomainCount * 4]));
    }

    private static CompactTopologyLayout ResolveCompactTopologyLayout(
        int payloadLength,
        int topologyPacketOffset,
        Ps2VifPacketSpan packet,
        int templateVifDataLength,
        byte[]? templateVifTextureData)
    {
        const int guardPaddingBytes = 12;
        var textureOverlapBytes = ResolveTemplateTextureOverlapBytes(packet, templateVifDataLength, templateVifTextureData);
        var aligned = Align(payloadLength + guardPaddingBytes + textureOverlapBytes, 4);
        while ((topologyPacketOffset + 4 + aligned - textureOverlapBytes) % 0x10 != 0)
        {
            aligned += 4;
        }

        return new CompactTopologyLayout(aligned, textureOverlapBytes);
    }

    private static int ResolveTemplateTextureOverlapBytes(
        Ps2VifPacketSpan packet,
        int templateVifDataLength,
        byte[]? templateVifTextureData)
    {
        if (templateVifTextureData is null || templateVifTextureData.Length == 0)
        {
            return 0;
        }

        var topologyEnd = packet.Offset + 4 + packet.PayloadLength;
        var overlapBytes = topologyEnd - templateVifDataLength;
        return overlapBytes is >= 0 and <= 12 && overlapBytes % 4 == 0
            ? overlapBytes
            : 0;
    }

    private static List<uint> BuildConservativeStripIndices(IReadOnlyList<uint> triangleIndices)
    {
        if (triangleIndices.Count < 3 || triangleIndices.Count % 3 != 0)
        {
            throw new InvalidDataException("glTF mesh indices must contain triangles.");
        }

        var stripIndices = new List<uint>();
        var currentStrip = new List<uint>();
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            if (a > 126 || b > 126 || c > 126)
            {
                throw new InvalidDataException("v1 importer supports mesh-local vertex indices up to 126.");
            }

            if (currentStrip.Count == 0)
            {
                stripIndices.Add(a);
                stripIndices.Add(b);
                stripIndices.Add(c);
                currentStrip.Add(a);
                currentStrip.Add(b);
                currentStrip.Add(c);
                continue;
            }

            if (TryAppendToCurrentStrip(currentStrip, a, b, c, out var nextVertex))
            {
                stripIndices.Add(nextVertex);
                currentStrip.Add(nextVertex);
                continue;
            }

            var last = currentStrip[^1];
            stripIndices.Add(last);
            stripIndices.Add(a);
            stripIndices.Add(a);
            stripIndices.Add(a);
            stripIndices.Add(b);
            stripIndices.Add(c);
            currentStrip.Clear();
            currentStrip.Add(a);
            currentStrip.Add(b);
            currentStrip.Add(c);
        }

        return stripIndices;
    }

    private static bool TryAppendToCurrentStrip(
        IReadOnlyList<uint> currentStrip,
        uint triangleA,
        uint triangleB,
        uint triangleC,
        out uint nextVertex)
    {
        nextVertex = 0;
        if (currentStrip.Count < 3)
        {
            return false;
        }

        var previousA = currentStrip[^2];
        var previousB = currentStrip[^1];
        var nextFlip = ((currentStrip.Count - 2) & 1) == 1;
        if (nextFlip)
        {
            if (triangleA != previousA || triangleC != previousB)
            {
                return false;
            }

            nextVertex = triangleB;
            return true;
        }

        if (triangleA != previousA || triangleB != previousB)
        {
            return false;
        }

        nextVertex = triangleC;
        return true;
    }


}
