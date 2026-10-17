using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static bool TryBuildTemplateTopologyReplacement(
        MobyMeshTableEntry templateEntry,
        IReadOnlyList<int> indexByOriginalIndex,
        IReadOnlyList<uint> expectedRemappedIndices,
        out VifBuildResult result)
    {
        result = default!;
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.Offset + 4 > combinedVifData.Length)
        {
            return false;
        }

        if (indexByOriginalIndex.Count != templateEntry.VertexCount)
        {
            return false;
        }

        var remappedCombined = (byte[])combinedVifData.Clone();
        var payloadOffset = packet.Offset + 4;
        var payloadLength = Math.Min(packet.PayloadLength, remappedCombined.Length - payloadOffset);
        if (payloadLength <= 0)
        {
            return false;
        }

        if (!TryRemapTopologyPayload(
            remappedCombined,
            payloadOffset,
            payloadLength,
            templateEntry.VifData.Length,
            templateEntry.VifTextureData?.Length ?? 0,
            indexByOriginalIndex))
        {
            return false;
        }

        SplitCombinedVifData(remappedCombined, templateEntry.VifData.Length, out var vifData, out var vifTextureData);

        result = new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: packet.PayloadLength,
            NewTopologyPayloadBytes: packet.PayloadLength,
            ReusedTemplateTopology: indexByOriginalIndex.Select((value, index) => value == index).All(match => match),
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: false,
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
        return true;
    }

    private static VifBuildResult BuildTemplateVifPassthrough(MobyMeshTableEntry templateEntry)
    {
        var packet = TryFindTopologyPacket(Combine(templateEntry.VifData, templateEntry.VifTextureData));
        return new VifBuildResult(
            (byte[])templateEntry.VifData.Clone(),
            templateEntry.VifTextureData is null ? null : (byte[])templateEntry.VifTextureData.Clone(),
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: packet?.PayloadLength ?? 0,
            NewTopologyPayloadBytes: packet?.PayloadLength ?? 0,
            ReusedTemplateTopology: true,
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: false,
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

    private static bool TryBuildRemappedTemplateTopology(
        MobyMeshTableEntry templateEntry,
        IReadOnlyList<int> indexByOriginalIndex,
        out List<uint> remappedIndices)
    {
        remappedIndices = [];
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.Offset + 4 > combinedVifData.Length)
        {
            return false;
        }

        var payloadOffset = packet.Offset + 4;
        var payloadLength = Math.Min(packet.PayloadLength, combinedVifData.Length - payloadOffset);
        if (payloadLength <= 0)
        {
            return false;
        }

        if (!TryDecodeTopologyPayload(
            combinedVifData.AsSpan(payloadOffset, payloadLength),
            templateEntry.VifTextureData,
            templateEntry.VertexCount,
            out var templateIndices))
        {
            return false;
        }

        remappedIndices = new List<uint>(templateIndices.Count);
        foreach (var index in templateIndices)
        {
            if (index >= indexByOriginalIndex.Count)
            {
                return false;
            }

            remappedIndices.Add(checked((uint)indexByOriginalIndex[(int)index]));
        }

        return true;
    }

    private static bool TryRemapTopologyPayload(
        byte[] combinedVifData,
        int indexPayloadOffset,
        int indexPayloadLength,
        int vifDataLength,
        int vifTextureDataLength,
        IReadOnlyList<int> indexByOriginalIndex)
    {
        var indexPayload = combinedVifData.AsSpan(indexPayloadOffset, indexPayloadLength);
        if (indexPayload.Length >= 3 && !TryRemapTopologyIndexByte(ref indexPayload[2], indexByOriginalIndex))
        {
            return false;
        }

        for (var i = 4; i < indexPayload.Length; i++)
        {
            if (!TryRemapTopologyIndexByte(ref indexPayload[i], indexByOriginalIndex))
            {
                return false;
            }
        }

        for (var textureOffset = 0x0C; textureOffset < vifTextureDataLength; textureOffset += 0x10)
        {
            var combinedOffset = vifDataLength + textureOffset;
            if (combinedOffset >= indexPayloadOffset && combinedOffset < indexPayloadOffset + indexPayloadLength)
            {
                continue;
            }

            if (!TryRemapTopologyIndexByte(ref combinedVifData[combinedOffset], indexByOriginalIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRemapTopologyIndexByte(ref byte value, IReadOnlyList<int> indexByOriginalIndex)
    {
        if (value == 0)
        {
            return true;
        }

        var oldIndex = (value & 0x7F) - 1;
        if (oldIndex < 0)
        {
            return true;
        }

        if (oldIndex >= indexByOriginalIndex.Count)
        {
            return false;
        }

        var newIndex = indexByOriginalIndex[oldIndex];
        if (newIndex < 0 || newIndex > 126)
        {
            return false;
        }

        value = checked((byte)((value & 0x80) | (newIndex + 1)));
        return true;
    }

    private static bool TryDecodeTopologyPayload(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount,
        out List<uint> indices)
    {
        indices = [];
        if (indexPayload.Length < 8 || positionCount < 3)
        {
            return false;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        List<uint>? currentStrip = null;
        var strips = new List<List<uint>>();
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var idx = unchecked((sbyte)indexPayload[j]);
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    if (currentStrip is null || currentStrip.Count < 3)
                    {
                        break;
                    }

                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = [];
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Add(currentStrip[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = [];
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Count >= 3))
        {
            var flip = false;
            for (var k = 2; k < strip.Count; k++)
            {
                var a = strip[k - 2];
                var b = strip[k - 1];
                var c = strip[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;

                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    continue;
                }

                var key = BuildIndexTriangleKey(i0, i1, i2);
                if (!seenTriangles.Add(key))
                {
                    continue;
                }

                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
            }
        }

        return indices.Count >= 3;
    }

    private static bool TryDecodeTopologyPayloadStrips(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount,
        out List<List<uint>> stripTriangles)
    {
        stripTriangles = [];
        if (indexPayload.Length < 8 || positionCount < 3)
        {
            return false;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        List<uint>? currentStrip = null;
        var strips = new List<List<uint>>();
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var idx = unchecked((sbyte)indexPayload[j]);
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = [];
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Add(currentStrip[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = [];
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Count >= 3))
        {
            var triangles = new List<uint>();
            var flip = false;
            for (var k = 2; k < strip.Count; k++)
            {
                var a = strip[k - 2];
                var b = strip[k - 1];
                var c = strip[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;

                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    continue;
                }

                var key = BuildIndexTriangleKey(i0, i1, i2);
                if (!seenTriangles.Add(key))
                {
                    continue;
                }

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);
            }

            if (triangles.Count > 0)
            {
                stripTriangles.Add(triangles);
            }
        }

        return stripTriangles.Count > 0;
    }

    private static string BuildIndexTriangleKey(uint a, uint b, uint c)
    {
        Span<uint> values = [a, b, c];
        values.Sort();
        return $"{values[0]}|{values[1]}|{values[2]}";
    }

    private static bool TriangleListsMatch(IReadOnlyList<uint> expected, IReadOnlyList<uint> actual)
    {
        if (expected.Count != actual.Count || expected.Count % 3 != 0)
        {
            return false;
        }

        var expectedCounts = CountTriangleSets(expected);
        var actualCounts = CountTriangleSets(actual);
        if (expectedCounts.Count != actualCounts.Count)
        {
            return false;
        }

        foreach (var (key, count) in expectedCounts)
        {
            if (!actualCounts.TryGetValue(key, out var actualCount) || actualCount != count)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TriangleSetContainsAll(IReadOnlyList<uint> actual, IReadOnlyList<uint> expected)
    {
        if (actual.Count % 3 != 0 || expected.Count % 3 != 0)
        {
            return false;
        }

        var actualCounts = CountTriangleSets(actual);
        var expectedCounts = CountTriangleSets(expected);
        foreach (var (key, count) in expectedCounts)
        {
            if (!actualCounts.TryGetValue(key, out var actualCount) || actualCount < count)
            {
                return false;
            }
        }

        return true;
    }

}
