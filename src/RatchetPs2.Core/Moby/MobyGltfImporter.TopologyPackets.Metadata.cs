using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static VifBuildResult BuildMetadataVifData(MobyMeshTableEntry templateEntry, ImportedMesh mesh)
    {
        var topology = mesh.Metadata?.Topology;
        if (topology is null)
        {
            return BuildTemplateVifPassthrough(templateEntry);
        }

        var alignedPayloadBytes = BuildMetadataAlignedTopologyPayload(topology);
        if (alignedPayloadBytes.Length == 0)
        {
            return BuildTemplateVifPassthrough(templateEntry);
        }

        using var stream = new MemoryStream(
            topology.BeforePacketBytes.Length
            + 4
            + alignedPayloadBytes.Length
            + topology.AfterPacketBytes.Length);
        stream.Write(topology.BeforePacketBytes);
        Ps2VifPacket.WriteHeader(
            stream,
            checked((ushort)topology.Immediate),
            checked((byte)(alignedPayloadBytes.Length / 4)),
            checked((byte)topology.CommandByte));
        stream.Write(alignedPayloadBytes);
        stream.Write(topology.AfterPacketBytes);

        var combinedVifData = stream.ToArray();
        SplitCombinedVifData(combinedVifData, topology.VifDataSplitOffset, out var vifData, out var vifTextureData);

        return new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: alignedPayloadBytes.Length,
            NewTopologyPayloadBytes: alignedPayloadBytes.Length,
            ReusedTemplateTopology: true,
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: true,
            GeneratedTopologyFromGltf: false,
            GeneratedTopologyTokenCount: 0,
            GeneratedTopologySourceTriangleCount: 0,
            GeneratedTopologyPayloadFitsMetadata: true,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null);
    }

    private static VifBuildResult BuildMetadataShapeTopologyVifData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        IReadOnlyList<uint> remappedTriangleIndices,
        bool allowExactSourceShapeMismatch = false,
        bool preferExactSourceTopology = false)
    {
        var topology = mesh.Metadata?.Topology ?? TryBuildTemplateTopologyMetadata(templateEntry);
        if (topology is null)
        {
            return BuildVifData(templateEntry.VifData, templateEntry.VifTextureData, remappedTriangleIndices);
        }

        var templateTraceForSourceFilter = TraceTopology(BuildResolvedTopologyTraceTokens(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount));
        var topologySourceTriangleIndices = TryBuildTemplateCompatibleTopologySource(
            templateTraceForSourceFilter,
            remappedTriangleIndices,
            out var templateCompatibleTriangleIndices)
            ? templateCompatibleTriangleIndices
            : remappedTriangleIndices;
        var topologyTokens = !preferExactSourceTopology
            && TryBuildTemplateSegmentedTopologyTokens(
                topology.AlignedPayloadBytes,
                topology.PayloadTokens,
                templateEntry.VifTextureData,
                templateEntry.VertexCount,
                topologySourceTriangleIndices,
                out var templateSegmentedTokens)
                    ? templateSegmentedTokens
                    : Ps2VifTopology.BuildRestartStripTokens(topologySourceTriangleIndices);
        var prefixBytes = topology.PayloadPrefixBytes.Count >= 4
            ? topology.PayloadPrefixBytes.Take(4).Select(value => checked((byte)value)).ToArray()
            : [0, 0, 0x80, 0];
        var adjustedVifTextureData = templateEntry.VifTextureData is null
            ? null
            : (byte[])templateEntry.VifTextureData.Clone();
        ApplyTemplateZeroMarkers(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount,
            topologyTokens,
            prefixBytes,
            adjustedVifTextureData,
            rewriteSecrets: true);
        var payload = Ps2VifTopology.BuildPayload(topologyTokens, prefixBytes);
        var unpaddedPayloadLength = 4 + topologyTokens.Count;
        for (var i = unpaddedPayloadLength; i < payload.Length && i < topology.AlignedPayloadBytes.Length; i++)
        {
            payload[i] = topology.AlignedPayloadBytes[i];
        }
        var payloadFitsMetadata = payload.Length <= topology.AlignedPayloadBytes.Length;
        var analysisPayload = payloadFitsMetadata
            ? preferExactSourceTopology
                ? Enumerable.Repeat((byte)0x80, topology.AlignedPayloadBytes.Length).ToArray()
                : (byte[])topology.AlignedPayloadBytes.Clone()
            : payload;
        if (payloadFitsMetadata)
        {
            payload.CopyTo(analysisPayload, 0);
            if (payload.Length < topology.AlignedPayloadBytes.Length)
            {
                var templateTailPayload = (byte[])topology.AlignedPayloadBytes.Clone();
                payload.CopyTo(templateTailPayload, 0);
                if (TopologyZeroMarkerPositionsMatch(
                        topology.AlignedPayloadBytes,
                        templateTailPayload,
                        templateEntry.VifTextureData,
                        adjustedVifTextureData,
                        templateEntry.VertexCount)
                    && TopologyShapesMatch(
                        SummarizeResolvedTopologyShape(BuildResolvedTopologyTraceTokens(topology.AlignedPayloadBytes, templateEntry.VifTextureData, templateEntry.VertexCount)),
                        SummarizeResolvedTopologyShape(BuildResolvedTopologyTraceTokens(templateTailPayload, adjustedVifTextureData, templateEntry.VertexCount))))
                {
                    analysisPayload = templateTailPayload;
                }
            }
        }

        var preservesTemplateControlMarkers = TopologyZeroMarkerPositionsMatch(
            topology.AlignedPayloadBytes,
            analysisPayload,
            templateEntry.VifTextureData,
            adjustedVifTextureData,
            templateEntry.VertexCount);
        var templateResolvedTokens = BuildResolvedTopologyTraceTokens(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount);
        var generatedResolvedTokens = BuildResolvedTopologyTraceTokens(
            analysisPayload,
            adjustedVifTextureData,
            templateEntry.VertexCount);
        var templateRestartCount = CountResolvedTopologyRestarts(templateResolvedTokens);
        var generatedRestartCount = CountResolvedTopologyRestarts(generatedResolvedTokens);
        var templateNegativeTokenCount = templateResolvedTokens.Count(token => token.IsNonPositive);
        var generatedNegativeTokenCount = generatedResolvedTokens.Count(token => token.IsNonPositive);
        var templateTopologyShape = SummarizeResolvedTopologyShape(templateResolvedTokens);
        var generatedTopologyShape = SummarizeResolvedTopologyShape(generatedResolvedTokens);
        var templateTopologyTrace = TraceTopology(templateResolvedTokens);
        var generatedTopologyTrace = TraceTopology(generatedResolvedTokens);
        var templateZeroMarkers = SummarizeTopologyZeroMarkers(templateResolvedTokens);
        var generatedZeroMarkers = SummarizeTopologyZeroMarkers(generatedResolvedTokens);
        var matchesTemplateControlShape = TopologyShapesMatch(templateTopologyShape, generatedTopologyShape);
        var topologyPayloadDiff = BuildTopologyPayloadDiff(topology.AlignedPayloadBytes, analysisPayload);
        var decodedTriangleIndices = new List<uint>();
        var payloadDecoded = TryDecodeTopologyPayload(
            analysisPayload,
            adjustedVifTextureData,
            templateEntry.VertexCount,
            out decodedTriangleIndices);
        var payloadMatchesSourceTriangles = payloadDecoded
            && TriangleListsMatch(topologySourceTriangleIndices, decodedTriangleIndices);
        var payloadContainsSourceTriangles = payloadDecoded
            && TriangleSetContainsAll(decodedTriangleIndices, topologySourceTriangleIndices);
        var topologySourceDiff = BuildTopologySourceDiff(
            templateTopologyTrace,
            remappedTriangleIndices,
            payloadDecoded ? decodedTriangleIndices : null);
        if (!preservesTemplateControlMarkers)
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = payloadFitsMetadata,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = false,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var allowRelaxedShapeMismatch = allowExactSourceShapeMismatch
            && payloadFitsMetadata
            && preservesTemplateControlMarkers
            && payloadMatchesSourceTriangles;
        if (!matchesTemplateControlShape && !allowRelaxedShapeMismatch)
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = payloadFitsMetadata,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = false,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var smallExpandedPayload = payload.Length <= topology.AlignedPayloadBytes.Length + 0x10;
        if (!payloadFitsMetadata
            && !(smallExpandedPayload && preservesTemplateControlMarkers && payloadMatchesSourceTriangles))
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = false,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var preservedZeroMarkerControlFlow = payloadFitsMetadata
            && preservesTemplateControlMarkers
            && matchesTemplateControlShape
            && templateZeroMarkers.Count > 0;
        if (!payloadMatchesSourceTriangles
            && !(preservedZeroMarkerControlFlow
                || (preservesTemplateControlMarkers && matchesTemplateControlShape && payloadContainsSourceTriangles)
                || (allowExactSourceShapeMismatch && preservesTemplateControlMarkers && payloadMatchesSourceTriangles)))
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = true,
                GeneratedTopologyMatchesSourceTriangles = false,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var alignedPayloadBytes = analysisPayload;

        using var stream = new MemoryStream(
            topology.BeforePacketBytes.Length
            + 4
            + alignedPayloadBytes.Length
            + topology.AfterPacketBytes.Length);
        stream.Write(topology.BeforePacketBytes);
        Ps2VifPacket.WriteHeader(
            stream,
            checked((ushort)topology.Immediate),
            checked((byte)(alignedPayloadBytes.Length / 4)),
            checked((byte)topology.CommandByte));
        stream.Write(alignedPayloadBytes);
        stream.Write(topology.AfterPacketBytes);

        var combinedVifData = stream.ToArray();
        var splitOffset = topology.VifDataSplitOffset;
        if (!payloadFitsMetadata && topology.Offset < topology.VifDataSplitOffset)
        {
            splitOffset += payload.Length - topology.AlignedPayloadBytes.Length;
        }

        SplitCombinedVifData(combinedVifData, splitOffset, out var vifData, out var vifTextureData);
        if (vifData.Length % 0x10 != 0)
        {
            Array.Resize(ref vifData, Align(vifData.Length, 0x10));
        }

        if (adjustedVifTextureData is not null)
        {
            vifTextureData = adjustedVifTextureData;
        }

        return new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: topologyTokens.Count - topologySourceTriangleIndices.Count,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: !payloadFitsMetadata,
            OriginalTopologyPayloadBytes: topology.AlignedPayloadBytes.Length,
            NewTopologyPayloadBytes: payload.Length,
            ReusedTemplateTopology: false,
            RemappedTemplateTopology: false,
            UsedMetadataTopologyLayout: true,
            GeneratedTopologyFromGltf: true,
            GeneratedTopologyTokenCount: topologyTokens.Count,
            GeneratedTopologySourceTriangleCount: topologySourceTriangleIndices.Count / 3,
            GeneratedTopologyPayloadFitsMetadata: payloadFitsMetadata,
            GeneratedTopologyMatchesSourceTriangles: payloadMatchesSourceTriangles,
            GeneratedTopologyPreservesTemplateControlMarkers: preservesTemplateControlMarkers,
            GeneratedTopologyMatchesTemplateControlShape: matchesTemplateControlShape,
            TemplateTopologyRestartCount: templateRestartCount,
            GeneratedTopologyRestartCount: generatedRestartCount,
            TemplateTopologyNegativeTokenCount: templateNegativeTokenCount,
            GeneratedTopologyNegativeTokenCount: generatedNegativeTokenCount,
            TemplateTopologyShape: templateTopologyShape,
            GeneratedTopologyShape: generatedTopologyShape,
            TemplateTopologyTrace: templateTopologyTrace,
            GeneratedTopologyTrace: generatedTopologyTrace,
            TemplateTopologyZeroMarkers: templateZeroMarkers,
            GeneratedTopologyZeroMarkers: generatedZeroMarkers,
            TopologySourceDiff: topologySourceDiff,
            TopologyPayloadDiff: topologyPayloadDiff);
    }

    private static ImportedTopologyMetadata? TryBuildTemplateTopologyMetadata(MobyMeshTableEntry templateEntry)
    {
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.PayloadLength <= 0 || packet.Offset + packet.TotalLength > combinedVifData.Length)
        {
            return null;
        }

        var payloadOffset = packet.Offset + 4;
        var payloadBytes = combinedVifData.AsSpan(payloadOffset, packet.PayloadLength).ToArray();
        var prefixBytes = payloadBytes.Take(4).Select(value => (int)value).ToList();
        var payloadTokens = new List<ImportedTopologyPayloadToken>();
        for (var i = 4; i < payloadBytes.Length; i++)
        {
            var value = payloadBytes[i];
            if (value == 0)
            {
                payloadTokens.Add(new ImportedTopologyPayloadToken("zero", Negative: false, VertexIndex: -1));
                continue;
            }

            var negative = (value & 0x80) != 0;
            var vertexIndex = (value & 0x7F) - 1;
            payloadTokens.Add(new ImportedTopologyPayloadToken(
                negative ? "negative_index" : "index",
                negative,
                vertexIndex));
        }

        return new ImportedTopologyMetadata(
            packet.Offset,
            packet.Immediate,
            packet.CommandByte,
            templateEntry.VifData.Length,
            Convert.ToBase64String(payloadBytes),
            payloadBytes,
            PayloadPaddingBytes: [],
            payloadBytes.Select(value => (int)value).ToList(),
            prefixBytes,
            payloadTokens,
            combinedVifData.AsSpan(0, packet.Offset).ToArray(),
            combinedVifData.AsSpan(packet.Offset + packet.TotalLength).ToArray());
    }

    private static byte[] BuildMetadataAlignedTopologyPayload(ImportedTopologyMetadata topology)
    {
        var payloadBytes = BuildMetadataTopologyPayload(topology);
        if (payloadBytes.Length == 0 && topology.PayloadBytes.Count == 0)
        {
            return topology.AlignedPayloadBytes;
        }

        var payloadLength = payloadBytes.Length + topology.PayloadPaddingBytes.Length;
        if (payloadLength == 0 || payloadLength % 4 != 0)
        {
            return [];
        }

        var result = new byte[payloadLength];
        Buffer.BlockCopy(payloadBytes, 0, result, 0, payloadBytes.Length);
        topology.PayloadPaddingBytes.CopyTo(result.AsSpan(payloadBytes.Length));
        return result;
    }

    private static byte[] BuildMetadataTopologyPayload(ImportedTopologyMetadata topology)
    {
        if (topology.PayloadPrefixBytes.Count >= 4 && topology.PayloadTokens.Count > 0)
        {
            var result = new byte[4 + topology.PayloadTokens.Count];
            for (var i = 0; i < 4; i++)
            {
                result[i] = checked((byte)topology.PayloadPrefixBytes[i]);
            }

            for (var i = 0; i < topology.PayloadTokens.Count; i++)
            {
                var token = topology.PayloadTokens[i];
                result[4 + i] = token.Kind == "zero"
                    ? (byte)0
                    : checked((byte)((token.Negative ? 0x80 : 0x00) | ((token.VertexIndex + 1) & 0x7F)));
            }

            return result;
        }

        if (topology.PayloadBytes.Count == 0)
        {
            return [];
        }

        var payload = new byte[topology.PayloadBytes.Count];
        for (var i = 0; i < topology.PayloadBytes.Count; i++)
        {
            payload[i] = checked((byte)topology.PayloadBytes[i]);
        }

        return payload;
    }

    private static void ApplyTemplateZeroMarkers(
        ReadOnlySpan<byte> templatePayload,
        byte[]? templateTexturePayload,
        int positionCount,
        List<byte> topologyTokens,
        byte[] prefixBytes,
        byte[]? adjustedTexturePayload,
        bool rewriteSecrets)
    {
        var templateTokens = BuildResolvedTopologyTraceTokens(templatePayload, templateTexturePayload, positionCount);
        var zeroMarkerOrdinal = 0;
        for (var tokenIndex = 0; tokenIndex < templateTokens.Count && tokenIndex < topologyTokens.Count; tokenIndex++)
        {
            if (!templateTokens[tokenIndex].IsZeroMarker)
            {
                continue;
            }

            var rawToken = unchecked((sbyte)topologyTokens[tokenIndex]);
            if (rawToken == 0)
            {
                zeroMarkerOrdinal++;
                continue;
            }

            topologyTokens[tokenIndex] = 0;
            if (!rewriteSecrets)
            {
                zeroMarkerOrdinal++;
                continue;
            }

            var secret = unchecked((byte)(rawToken + 0x80));
            if (!TryWriteZeroMarkerSecret(zeroMarkerOrdinal, secret, prefixBytes, adjustedTexturePayload))
            {
                zeroMarkerOrdinal++;
                continue;
            }

            zeroMarkerOrdinal++;
        }
    }

    private static bool TryWriteZeroMarkerSecret(
        int zeroMarkerOrdinal,
        byte secret,
        byte[] prefixBytes,
        byte[]? adjustedTexturePayload)
    {
        if (zeroMarkerOrdinal == 0)
        {
            if (prefixBytes.Length < 3)
            {
                return false;
            }

            prefixBytes[2] = secret;
            return true;
        }

        var textureSecretOffset = (zeroMarkerOrdinal - 1) * 0x10 + 0x0C;
        if (adjustedTexturePayload is null || textureSecretOffset >= adjustedTexturePayload.Length)
        {
            return false;
        }

        adjustedTexturePayload[textureSecretOffset] = secret;
        return true;
    }

}
