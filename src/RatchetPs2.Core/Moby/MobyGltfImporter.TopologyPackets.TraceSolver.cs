using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static bool TryBuildTracePatternedStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<TopologyTraceToken> tokenPattern,
        TopologyTraceSegment traceSegment,
        IReadOnlyList<uint> sourceVertices,
        out List<byte> tokens,
        out bool[] usedTriangleMask,
        int? requiredUsedTriangleCount = null)
    {
        tokens = [];
        usedTriangleMask = [];
        var expectedUsedTriangleCount = requiredUsedTriangleCount ?? triangles.Count;
        if (expectedUsedTriangleCount > triangles.Count
            || expectedUsedTriangleCount > traceSegment.UniqueTriangleCount
            || tokenPattern.Count != traceSegment.TokenCount)
        {
            return false;
        }

        var strictTrace = triangles.Count == traceSegment.UniqueTriangleCount;
        var templateOnlyTriangleCount = traceSegment.UniqueTriangleCount - expectedUsedTriangleCount;
        var expectedDegenerateCounts = new int[tokenPattern.Count];
        foreach (var controlEvent in traceSegment.ControlEvents)
        {
            if (controlEvent.SegmentTokenIndex < 0 || controlEvent.SegmentTokenIndex >= expectedDegenerateCounts.Length)
            {
                return false;
            }

            expectedDegenerateCounts[controlEvent.SegmentTokenIndex] += controlEvent.EmittedTriangles.Count(triangle => triangle.IsDegenerate);
        }

        var memo = new HashSet<string>(StringComparer.Ordinal);
        var searchBudget = 1000000;
        bool[]? solvedUsedTriangles = null;
        var solved = TrySolveTracePatternedStrip(
            triangles,
            tokenPattern,
            expectedDegenerateCounts,
            traceSegment.DegenerateTriangleCount,
            templateOnlyTriangleCount,
            expectedUsedTriangleCount,
            sourceVertices,
            tokenIndex: 0,
            strip: [],
            usedTriangles: new bool[triangles.Count],
            degenerateTriangleCount: 0,
            templateOnlyTriangleCount: 0,
            strictTrace,
            tokens,
            memo,
            ref solvedUsedTriangles,
            ref searchBudget);
        usedTriangleMask = solvedUsedTriangles ?? [];
        return solved;
    }

    private static bool TrySolveTracePatternedStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<TopologyTraceToken> tokenPattern,
        IReadOnlyList<int> expectedDegenerateCounts,
        int expectedTotalDegenerateCount,
        int expectedTemplateOnlyTriangleCount,
        int expectedUsedTriangleCount,
        IReadOnlyList<uint> sourceVertices,
        int tokenIndex,
        List<uint> strip,
        bool[] usedTriangles,
        int degenerateTriangleCount,
        int templateOnlyTriangleCount,
        bool strictTrace,
        List<byte> tokens,
        HashSet<string> memo,
        ref bool[]? solvedUsedTriangles,
        ref int searchBudget)
    {
        if (searchBudget-- <= 0)
        {
            return false;
        }

        if (tokenIndex == tokenPattern.Count)
        {
            if (usedTriangles.Count(used => used) == expectedUsedTriangleCount
                && degenerateTriangleCount == expectedTotalDegenerateCount
                && templateOnlyTriangleCount == expectedTemplateOnlyTriangleCount)
            {
                solvedUsedTriangles = (bool[])usedTriangles.Clone();
                return true;
            }

            return false;
        }

        var lastA = strip.Count >= 2 ? strip[^2] : 127u;
        var lastB = strip.Count >= 1 ? strip[^1] : 127u;
        var usedTriangleKey = BuildUsedTriangleKey(usedTriangles);
        var memoKey = $"{tokenIndex}|{lastA}|{lastB}|{usedTriangleKey}|{degenerateTriangleCount}|{templateOnlyTriangleCount}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var token = tokenPattern[tokenIndex];
        var isNegative = token.IsNonPositive;
        var nextIsRestart = isNegative
            && tokenIndex + 1 < tokenPattern.Count
            && tokenPattern[tokenIndex + 1].IsNonPositive;
        var candidates = token.IsZeroMarker
            ? new[] { (uint)token.VertexIndex }
            : GetTracePatternCandidates(
                triangles,
                FirstUnusedTriangleIndex(usedTriangles),
                sourceVertices,
                token.VertexIndex is >= 0 and <= 126 ? (uint)token.VertexIndex : null);
        foreach (var candidate in candidates)
        {
            var nextStrip = new List<uint>(strip);
            var nextUsedTriangles = (bool[])usedTriangles.Clone();
            var tokenDegenerateCount = 0;
            var nextDegenerateTriangleCount = degenerateTriangleCount;
            var nextTemplateOnlyTriangleCount = templateOnlyTriangleCount;

            if (isNegative && !nextIsRestart)
            {
                if (nextStrip.Count == 0)
                {
                    continue;
                }

                nextStrip.Add(nextStrip[^1]);
                if (!TryValidateTraceTriangle(
                    nextStrip,
                    triangles,
                    nextUsedTriangles,
                    ref tokenDegenerateCount,
                    ref nextDegenerateTriangleCount,
                    ref nextTemplateOnlyTriangleCount,
                    expectedTemplateOnlyTriangleCount))
                {
                    continue;
                }
            }

            nextStrip.Add(candidate);
            if (!TryValidateTraceTriangle(
                nextStrip,
                triangles,
                nextUsedTriangles,
                ref tokenDegenerateCount,
                ref nextDegenerateTriangleCount,
                ref nextTemplateOnlyTriangleCount,
                expectedTemplateOnlyTriangleCount))
            {
                continue;
            }

            if (tokenDegenerateCount != expectedDegenerateCounts[tokenIndex])
            {
                continue;
            }

            tokens.Add(token.IsZeroMarker ? (byte)0 : Ps2VifTopology.EncodeIndexToken(candidate, isNegative));
            if (TrySolveTracePatternedStrip(
                triangles,
                tokenPattern,
                expectedDegenerateCounts,
                expectedTotalDegenerateCount,
                expectedTemplateOnlyTriangleCount,
                expectedUsedTriangleCount,
                sourceVertices,
                tokenIndex + 1,
                nextStrip,
                nextUsedTriangles,
                nextDegenerateTriangleCount,
                nextTemplateOnlyTriangleCount,
                strictTrace,
                tokens,
                memo,
                ref solvedUsedTriangles,
                ref searchBudget))
            {
                return true;
            }

            tokens.RemoveAt(tokens.Count - 1);
        }

        return false;
    }

    private static int FirstUnusedTriangleIndex(IReadOnlyList<bool> usedTriangles)
    {
        for (var i = 0; i < usedTriangles.Count; i++)
        {
            if (!usedTriangles[i])
            {
                return i;
            }
        }

        return usedTriangles.Count;
    }

    private static string BuildUsedTriangleKey(IReadOnlyList<bool> usedTriangles)
    {
        if (usedTriangles.Count == 0)
        {
            return string.Empty;
        }

        var chars = new char[(usedTriangles.Count + 3) / 4];
        for (var i = 0; i < usedTriangles.Count; i++)
        {
            if (!usedTriangles[i])
            {
                continue;
            }

            chars[i / 4] = (char)(chars[i / 4] | (char)(1 << (i % 4)));
        }

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)('A' + chars[i]);
        }

        return new string(chars);
    }

    private static IEnumerable<uint> GetTracePatternCandidates(
        IReadOnlyList<TopologyTriangle> triangles,
        int triangleIndex,
        IReadOnlyList<uint> sourceVertices,
        uint? preferredVertex)
    {
        var yielded = new HashSet<uint>();
        if (preferredVertex is { } preferred && yielded.Add(preferred))
        {
            yield return preferred;
        }

        if (triangleIndex < triangles.Count)
        {
            var triangle = triangles[triangleIndex];
            if (yielded.Add(triangle.A))
            {
                yield return triangle.A;
            }

            if (yielded.Add(triangle.B))
            {
                yield return triangle.B;
            }

            if (yielded.Add(triangle.C))
            {
                yield return triangle.C;
            }
        }

        if (triangleIndex + 1 < triangles.Count)
        {
            var triangle = triangles[triangleIndex + 1];
            if (yielded.Add(triangle.A))
            {
                yield return triangle.A;
            }

            if (yielded.Add(triangle.B))
            {
                yield return triangle.B;
            }

            if (yielded.Add(triangle.C))
            {
                yield return triangle.C;
            }
        }

        foreach (var vertex in sourceVertices)
        {
            if (yielded.Add(vertex))
            {
                yield return vertex;
            }
        }
    }

    private static bool TryValidateTraceTriangle(
        IReadOnlyList<uint> strip,
        IReadOnlyList<TopologyTriangle> triangles,
        bool[] usedTriangles,
        ref int tokenDegenerateCount,
        ref int totalDegenerateCount,
        ref int templateOnlyTriangleCount,
        int expectedTemplateOnlyTriangleCount)
    {
        var k = strip.Count - 1;
        if (k < 2)
        {
            return true;
        }

        var a = strip[k - 2];
        var b = strip[k - 1];
        var c = strip[k];
        var flip = ((k - 2) & 1) == 1;
        var i0 = a;
        var i1 = flip ? c : b;
        var i2 = flip ? b : c;
        if (i0 == i1 || i1 == i2 || i0 == i2)
        {
            tokenDegenerateCount++;
            totalDegenerateCount++;
            return true;
        }

        var matchingTriangleIndex = FindUnusedTriangleIndex(triangles, usedTriangles, i0, i1, i2);
        if (matchingTriangleIndex < 0)
        {
            if (templateOnlyTriangleCount >= expectedTemplateOnlyTriangleCount)
            {
                return false;
            }

            templateOnlyTriangleCount++;
            return true;
        }

        usedTriangles[matchingTriangleIndex] = true;
        return true;
    }

    private static int FindUnusedTriangleIndex(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> usedTriangles,
        uint a,
        uint b,
        uint c)
    {
        for (var i = 0; i < triangles.Count; i++)
        {
            if (usedTriangles[i])
            {
                continue;
            }

            var triangle = triangles[i];
            if (triangle.A == a && triangle.B == b && triangle.C == c)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<List<bool>> BuildTemplateTopologyTokenPatterns(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var effectiveTokenCount = tokens.Count;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.Equals(tokens[i].Kind, "zero", StringComparison.Ordinal))
            {
                continue;
            }

            effectiveTokenCount = Math.Max(0, i - 3);
            break;
        }

        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < effectiveTokenCount; i++)
        {
            var isNegative = tokens[i].Negative;
            var nextIsNonPositive = i + 1 < effectiveTokenCount && IsNonPositiveTopologyToken(tokens[i + 1]);
            if (IsNonPositiveTopologyToken(tokens[i]) && nextIsNonPositive)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(isNegative);
        }

        return patterns.Where(pattern => pattern.Count > 0).ToList();
    }

    private static List<List<TopologyTraceToken>> BuildResolvedTopologyTokenPatterns(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = BuildResolvedTopologyTraceTokens(indexPayload, texturePayload, positionCount);
        var patterns = new List<List<TopologyTraceToken>>();
        List<TopologyTraceToken>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var nextIsNonPositive = i + 1 < tokens.Count && tokens[i + 1].IsNonPositive;
            if (tokens[i].IsNonPositive && nextIsNonPositive)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(tokens[i]);
        }

        return patterns.Where(pattern => pattern.Count > 0).ToList();
    }

    private static List<TopologyTraceToken> BuildResolvedTopologyTraceTokens(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = new List<TopologyTraceToken>();
        if (indexPayload.Length < 8 || positionCount < 1)
        {
            return tokens;
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
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var raw = unchecked((sbyte)indexPayload[j]);
            var idx = raw;
            var isZeroMarker = false;
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    if (tokens.Count >= 3)
                    {
                        tokens.RemoveRange(tokens.Count - 3, 3);
                    }

                    break;
                }

                idx = (sbyte)(secret - 0x80);
                isZeroMarker = true;
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            var vertexIndex = (idx & 0x7F) - 1;
            if (vertexIndex < 0 || vertexIndex >= positionCount)
            {
                break;
            }

            tokens.Add(new TopologyTraceToken(idx <= 0, vertexIndex, isZeroMarker));
        }

        return tokens;
    }

}
