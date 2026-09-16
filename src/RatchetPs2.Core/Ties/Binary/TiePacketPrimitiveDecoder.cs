namespace RatchetPs2.Core.Ties;

internal static class TiePacketPrimitiveDecoder
{
    private const int AdGifPacketQwordCount = 6;
    private const int GsVertexPacketQwordCount = 3;

    public static List<TiePacketPrimitive> DecodePacketPrimitives(
        IReadOnlyList<TiePacketSetupRow> setupRows,
        IReadOnlyList<TiePacketStripControl> stripControls,
        IReadOnlyList<TiePacketDecodedVertex> decodedVertices,
        bool useStripTokenReferences)
    {
        if (stripControls.Count == 0
            || decodedVertices.Count == 0
            || setupRows.Count < TiePacketControlDecoder.PacketSetupQwordCount)
        {
            return [];
        }

        var adGifDestOffsets = ReadSetupOffsets(setupRows[0]);
        var adGifSourceOffsets = ReadSetupOffsets(setupRows[1]);
        if (adGifDestOffsets.Length == 0 || adGifSourceOffsets.Length == 0)
        {
            return [];
        }

        var vertices = BuildPacketVertexReferences(decodedVertices);
        if (vertices.Count == 0)
        {
            return [];
        }

        var verticesByOffset = new Dictionary<int, TiePacketVertexReference>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            verticesByOffset[vertices[i].GsPacketWriteOffset] = vertices[i];
        }
        var primitives = new List<TiePacketPrimitive>();
        var currentVertices = new List<TiePacketVertexReference>();
        var materialIndex = adGifSourceOffsets[0] / TieShader.Size;
        var nextStrip = 0;
        var nextVertex = 0;
        var nextAdGif = 1;
        var nextOffset = AdGifPacketQwordCount;

        while (nextStrip < stripControls.Count || nextVertex < vertices.Count)
        {
            if (nextStrip < stripControls.Count && stripControls[nextStrip].VuAddress == nextOffset)
            {
                var stripControl = stripControls[nextStrip];
                currentVertices = useStripTokenReferences
                    ? BuildStripDrawVertexReferences(stripControl, verticesByOffset)
                    : [];
                if (useStripTokenReferences && currentVertices.Count != stripControl.TokenCount)
                {
                    return [];
                }

                primitives.Add(new TiePacketPrimitive
                {
                    Index = primitives.Count,
                    PacketStripIndex = stripControl.Index,
                    MaterialIndex = materialIndex,
                    WindingOrder = (stripControl.Flags & 0x20) != 0,
                    Vertices = currentVertices
                });

                nextStrip++;
                nextOffset += 1;
                continue;
            }

            if (nextVertex < vertices.Count && vertices[nextVertex].GsPacketWriteOffset == nextOffset)
            {
                if (!useStripTokenReferences)
                {
                    if (primitives.Count == 0)
                    {
                        return [];
                    }

                    currentVertices.Add(vertices[nextVertex]);
                }

                nextVertex++;
                nextOffset += GsVertexPacketQwordCount;
                continue;
            }

            if (nextAdGif < adGifSourceOffsets.Length
                && nextAdGif - 1 < adGifDestOffsets.Length
                && adGifDestOffsets[nextAdGif - 1] == nextOffset)
            {
                materialIndex = adGifSourceOffsets[nextAdGif] / TieShader.Size;
                nextAdGif++;
                nextOffset += AdGifPacketQwordCount;
                continue;
            }

            return [];
        }

        return primitives;
    }

    private static List<TiePacketVertexReference> BuildStripDrawVertexReferences(
        TiePacketStripControl stripControl,
        IReadOnlyDictionary<int, TiePacketVertexReference> verticesByOffset)
    {
        if (stripControl.DecodedTokens.Count != stripControl.TokenCount)
        {
            return [];
        }

        var references = new List<TiePacketVertexReference>(stripControl.DecodedTokens.Count);
        for (var i = 0; i < stripControl.DecodedTokens.Count; i++)
        {
            var token = stripControl.DecodedTokens[i];
            if (!token.ReferencedGsPacketWriteOffset.HasValue
                || !verticesByOffset.TryGetValue(token.ReferencedGsPacketWriteOffset.Value, out var reference))
            {
                return [];
            }

            references.Add(reference);
        }

        return references;
    }

    private static int[] ReadSetupOffsets(TiePacketSetupRow row)
    {
        var words = row.Words.ToArray();
        Array.Sort(words, static (left, right) => left.WordIndex.CompareTo(right.WordIndex));
        var offsets = GC.AllocateUninitializedArray<int>(words.Length);
        for (var i = 0; i < words.Length; i++)
        {
            offsets[i] = words[i].Raw;
        }
        return offsets;
    }

    private static List<TiePacketVertexReference> BuildPacketVertexReferences(
        IReadOnlyList<TiePacketDecodedVertex> decodedVertices)
    {
        var raw = new List<TiePacketVertexReference>();
        for (var vertexIndex = 0; vertexIndex < decodedVertices.Count; vertexIndex++)
        {
            var vertex = decodedVertices[vertexIndex];
            raw.Add(new TiePacketVertexReference
            {
                Index = raw.Count,
                GsPacketWriteOffset = vertex.GsPacketWriteOffset,
                IsSecondaryWriteOffset = false,
                Vertex = vertex
            });

            if (vertex.SecondaryGsPacketWriteOffset != 0
                && vertex.SecondaryGsPacketWriteOffset != vertex.GsPacketWriteOffset)
            {
                raw.Add(new TiePacketVertexReference
                {
                    Index = raw.Count,
                    GsPacketWriteOffset = vertex.SecondaryGsPacketWriteOffset,
                    IsSecondaryWriteOffset = true,
                    Vertex = vertex
                });
            }
        }

        raw.Sort(static (left, right) =>
        {
            var comparison = left.GsPacketWriteOffset.CompareTo(right.GsPacketWriteOffset);
            if (comparison != 0) return comparison;
            comparison = left.Vertex.Index.CompareTo(right.Vertex.Index);
            return comparison != 0 ? comparison : left.IsSecondaryWriteOffset.CompareTo(right.IsSecondaryWriteOffset);
        });
        var unique = new List<TiePacketVertexReference>(raw.Count);
        for (var vertexIndex = 0; vertexIndex < raw.Count; vertexIndex++)
        {
            var vertex = raw[vertexIndex];
            if (unique.Count > 0 && unique[^1].GsPacketWriteOffset == vertex.GsPacketWriteOffset)
            {
                continue;
            }

            unique.Add(new TiePacketVertexReference
            {
                Index = unique.Count,
                GsPacketWriteOffset = vertex.GsPacketWriteOffset,
                IsSecondaryWriteOffset = vertex.IsSecondaryWriteOffset,
                Vertex = vertex.Vertex
            });
        }

        return unique;
    }
}
