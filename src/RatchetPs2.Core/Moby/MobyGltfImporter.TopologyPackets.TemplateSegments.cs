using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static bool TryBuildTemplateSegmentedTopologyTokens(
        ReadOnlySpan<byte> templatePayload,
        IReadOnlyList<ImportedTopologyPayloadToken> templateTokens,
        byte[]? texturePayload,
        int positionCount,
        IReadOnlyList<uint> triangleIndices,
        out List<byte> topologyTokens)
    {
        topologyTokens = [];
        if (!TryDecodeTopologyPayloadStrips(templatePayload, texturePayload, positionCount, out var templateStrips))
        {
            return false;
        }

        var templateTokenPatterns = BuildResolvedTopologyTokenPatterns(templatePayload, texturePayload, positionCount);
        if (templateTokenPatterns.Count != templateStrips.Count)
        {
            return false;
        }

        var sourceTriangles = new List<TopologyTriangle>(triangleIndices.Count / 3);
        for (var i = 0; i + 2 < triangleIndices.Count; i += 3)
        {
            sourceTriangles.Add(new TopologyTriangle(triangleIndices[i], triangleIndices[i + 1], triangleIndices[i + 2]));
        }

        var allSourceTriangleKeys = sourceTriangles
            .Select(triangle => BuildIndexTriangleKey(triangle.A, triangle.B, triangle.C))
            .ToHashSet(StringComparer.Ordinal);
        var allSourceVertices = sourceTriangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var allTraceVertices = templateTokenPatterns
            .SelectMany(pattern => pattern.Select(token => (uint)token.VertexIndex))
            .Where(vertexIndex => vertexIndex <= 126)
            .Distinct()
            .ToArray();
        var allCandidateVertices = allSourceVertices
            .Concat(allTraceVertices)
            .Distinct()
            .ToArray();
        var templateTrace = TraceTopology(templateTokenPatterns.SelectMany(pattern => pattern).ToList());
        var templateZeroMarkerCount = templateTokenPatterns.Sum(pattern => pattern.Count(token => token.IsZeroMarker));
        if (templateZeroMarkerCount > 1
            && templateTrace.Segments.Length <= 20
            && TryBuildTemplateTraceSegmentedTopologyTokens(
            templateTokenPatterns,
            templateTrace,
            sourceTriangles,
            allCandidateVertices,
            out var traceSegmentedTokens))
        {
            topologyTokens = traceSegmentedTokens;
            return true;
        }

        var sourceTriangleOffset = 0;
        for (var stripIndex = 0; stripIndex < templateStrips.Count; stripIndex++)
        {
            var templateStripTriangleCount = stripIndex < templateTrace.Segments.Length
                ? templateTrace.Segments[stripIndex].UniqueTriangleCount
                : templateStrips[stripIndex].Count / 3;
            var sourceStripTriangleCount = Math.Min(templateStripTriangleCount, sourceTriangles.Count - sourceTriangleOffset);
            if (sourceStripTriangleCount < 0)
            {
                return false;
            }

            var sourceStrip = sourceTriangles.GetRange(sourceTriangleOffset, sourceStripTriangleCount);
            sourceTriangleOffset += sourceStripTriangleCount;
            if (sourceStrip.Count == 0)
            {
                continue;
            }

            var tokenPattern = templateTokenPatterns[stripIndex];
            var boolTokenPattern = tokenPattern.Select(token => token.IsNonPositive).ToArray();
            if (!(stripIndex < templateTrace.Segments.Length
                    && TryBuildTracePatternedStripTokens(
                        sourceStrip,
                        tokenPattern,
                        templateTrace.Segments[stripIndex],
                        allCandidateVertices,
                        out var stripTokens,
                        out _))
                && !TryBuildPatternedStripTokens(sourceStrip, boolTokenPattern, allSourceTriangleKeys, out stripTokens)
                && !TryBuildSequentialStripTokens(sourceStrip, out stripTokens))
            {
                return false;
            }

            topologyTokens.AddRange(stripTokens);
        }

        return sourceTriangleOffset == sourceTriangles.Count;
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokens(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        TopologyTraceSummary templateTrace,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        out List<byte> topologyTokens)
    {
        topologyTokens = [];
        if (templateTokenPatterns.Count != templateTrace.Segments.Length)
        {
            return false;
        }

        var futureCapacity = new int[templateTrace.Segments.Length + 1];
        for (var i = templateTrace.Segments.Length - 1; i >= 0; i--)
        {
            futureCapacity[i] = futureCapacity[i + 1] + templateTrace.Segments[i].UniqueTriangleCount;
        }

        var memo = new HashSet<string>(StringComparer.Ordinal);
        return TryBuildTemplateTraceSegmentedTopologyTokens(
            templateTokenPatterns,
            templateTrace.Segments,
            sourceTriangles,
            sourceVertices,
            futureCapacity,
            segmentIndex: 0,
            sourceTriangleOffset: 0,
            topologyTokens,
            memo);
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokensUnordered(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        IReadOnlyList<TopologyTraceSegment> templateSegments,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlyList<int> futureCapacity,
        int segmentIndex,
        bool[] usedSourceTriangles,
        int usedSourceTriangleCount,
        List<byte> topologyTokens,
        HashSet<string> memo)
    {
        if (segmentIndex == templateSegments.Count)
        {
            return usedSourceTriangleCount == sourceTriangles.Count;
        }

        var memoKey = $"u|{segmentIndex}|{usedSourceTriangleCount}|{BuildUsedTriangleKey(usedSourceTriangles)}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var segment = templateSegments[segmentIndex];
        var tokenPattern = templateTokenPatterns[segmentIndex];
        var remainingSourceTriangles = sourceTriangles.Count - usedSourceTriangleCount;
        var minCount = Math.Max(0, remainingSourceTriangles - futureCapacity[segmentIndex + 1]);
        var maxCount = Math.Min(segment.UniqueTriangleCount, remainingSourceTriangles);
        var availableTriangles = new List<TopologyTriangle>();
        var availableIndices = new List<int>();
        for (var i = 0; i < sourceTriangles.Count; i++)
        {
            if (usedSourceTriangles[i])
            {
                continue;
            }

            availableTriangles.Add(sourceTriangles[i]);
            availableIndices.Add(i);
        }

        foreach (var count in BuildSegmentSourceCountCandidates(minCount, maxCount))
        {
            if (count > availableTriangles.Count)
            {
                continue;
            }

            if (!TryBuildTracePatternedStripTokens(
                    availableTriangles,
                    tokenPattern,
                    segment,
                    sourceVertices,
                    out var segmentTokens,
                    out var segmentUsedTriangles,
                    requiredUsedTriangleCount: count))
            {
                continue;
            }

            var nextUsedSourceTriangles = (bool[])usedSourceTriangles.Clone();
            var nextUsedSourceTriangleCount = usedSourceTriangleCount;
            for (var i = 0; i < segmentUsedTriangles.Length; i++)
            {
                if (!segmentUsedTriangles[i])
                {
                    continue;
                }

                var originalIndex = availableIndices[i];
                if (!nextUsedSourceTriangles[originalIndex])
                {
                    nextUsedSourceTriangles[originalIndex] = true;
                    nextUsedSourceTriangleCount++;
                }
            }

            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(segmentTokens);
            if (TryBuildTemplateTraceSegmentedTopologyTokensUnordered(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                nextUsedSourceTriangles,
                nextUsedSourceTriangleCount,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        return false;
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokens(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        IReadOnlyList<TopologyTraceSegment> templateSegments,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlyList<int> futureCapacity,
        int segmentIndex,
        int sourceTriangleOffset,
        List<byte> topologyTokens,
        HashSet<string> memo)
    {
        if (segmentIndex == templateSegments.Count)
        {
            return sourceTriangleOffset == sourceTriangles.Count;
        }

        var memoKey = $"{segmentIndex}|{sourceTriangleOffset}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var segment = templateSegments[segmentIndex];
        var tokenPattern = templateTokenPatterns[segmentIndex];
        var remainingSourceTriangles = sourceTriangles.Count - sourceTriangleOffset;
        var minCount = Math.Max(0, remainingSourceTriangles - futureCapacity[segmentIndex + 1]);
        var maxCount = Math.Min(segment.UniqueTriangleCount, remainingSourceTriangles);
        foreach (var count in BuildSegmentSourceCountCandidates(minCount, maxCount))
        {
            var sourceStrip = count == 0
                ? []
                : sourceTriangles.Skip(sourceTriangleOffset).Take(count).ToList();
            if (!TryBuildTracePatternedStripTokens(sourceStrip, tokenPattern, segment, sourceVertices, out var segmentTokens, out _))
            {
                continue;
            }

            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(segmentTokens);
            if (TryBuildTemplateTraceSegmentedTopologyTokens(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                sourceTriangleOffset + count,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        if (minCount == 0 && tokenPattern.Any(token => token.IsZeroMarker))
        {
            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(BuildTemplateTraceTokens(tokenPattern));
            if (TryBuildTemplateTraceSegmentedTopologyTokens(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                sourceTriangleOffset,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        return false;
    }

    private static IEnumerable<int> BuildSegmentSourceCountCandidates(int minCount, int maxCount)
    {
        if (maxCount < minCount)
        {
            yield break;
        }

        yield return maxCount;

        if (maxCount - 1 >= minCount)
        {
            yield return maxCount - 1;
        }

        if (minCount != maxCount && minCount != maxCount - 1)
        {
            yield return minCount;
        }
    }

    private static List<byte> BuildTemplateTraceTokens(IReadOnlyList<TopologyTraceToken> tokenPattern)
    {
        var tokens = new List<byte>(tokenPattern.Count);
        foreach (var token in tokenPattern)
        {
            tokens.Add(token.IsZeroMarker
                ? (byte)0
                : Ps2VifTopology.EncodeIndexToken((uint)token.VertexIndex, token.IsNonPositive));
        }

        return tokens;
    }

}
