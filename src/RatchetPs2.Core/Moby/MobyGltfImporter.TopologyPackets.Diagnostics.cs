using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static TopologySourceDiff BuildTopologySourceDiff(
        TopologyTraceSummary? templateTrace,
        IReadOnlyList<uint> sourceIndices,
        IReadOnlyList<uint>? generatedIndices)
    {
        var templateTriangleKeys = templateTrace is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : CountTriangleSets(templateTrace.UniqueTriangleIndices);
        var sourceTriangleKeys = CountTriangleSets(sourceIndices);
        var generatedTriangleKeys = generatedIndices is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : CountTriangleSets(generatedIndices);

        return new TopologySourceDiff(
            TemplateUniqueTriangleCount: templateTriangleKeys.Values.Sum(),
            SourceTriangleCount: sourceIndices.Count / 3,
            GeneratedDecodedTriangleCount: generatedIndices?.Count / 3 ?? 0,
            TemplateOnlyTriangleCount: CountOnly(templateTriangleKeys, sourceTriangleKeys),
            SourceOnlyTriangleCount: CountOnly(sourceTriangleKeys, templateTriangleKeys),
            GeneratedOnlyTriangleCount: CountOnly(generatedTriangleKeys, sourceTriangleKeys),
            SourceMissingFromGeneratedTriangleCount: CountOnly(sourceTriangleKeys, generatedTriangleKeys),
            TemplateVertexRange: BuildTriangleVertexRange(templateTrace is null ? [] : templateTrace.UniqueTriangleIndices),
            SourceVertexRange: BuildTriangleVertexRange(sourceIndices),
            GeneratedVertexRange: generatedIndices is null ? null : BuildTriangleVertexRange(generatedIndices),
            TemplateOnlyTriangleSamples: BuildOnlySamples(templateTriangleKeys, sourceTriangleKeys),
            SourceOnlyTriangleSamples: BuildOnlySamples(sourceTriangleKeys, templateTriangleKeys),
            GeneratedOnlyTriangleSamples: BuildOnlySamples(generatedTriangleKeys, sourceTriangleKeys),
            SourceMissingFromGeneratedTriangleSamples: BuildOnlySamples(sourceTriangleKeys, generatedTriangleKeys));
    }

    private static TopologyPayloadDiff BuildTopologyPayloadDiff(
        IReadOnlyList<byte> templatePayload,
        IReadOnlyList<byte> generatedPayload,
        int maxSamples = 16)
    {
        var sharedLength = Math.Min(templatePayload.Count, generatedPayload.Count);
        var differingByteCount = Math.Abs(templatePayload.Count - generatedPayload.Count);
        var samples = new List<TopologyPayloadByteDiff>(maxSamples);
        for (var i = 0; i < sharedLength; i++)
        {
            if (templatePayload[i] == generatedPayload[i])
            {
                continue;
            }

            differingByteCount++;
            if (samples.Count < maxSamples)
            {
                samples.Add(new TopologyPayloadByteDiff(i, templatePayload[i], generatedPayload[i]));
            }
        }

        for (var i = sharedLength; i < templatePayload.Count && samples.Count < maxSamples; i++)
        {
            samples.Add(new TopologyPayloadByteDiff(i, templatePayload[i], null));
        }

        for (var i = sharedLength; i < generatedPayload.Count && samples.Count < maxSamples; i++)
        {
            samples.Add(new TopologyPayloadByteDiff(i, null, generatedPayload[i]));
        }

        return new TopologyPayloadDiff(
            TemplatePayloadBytes: templatePayload.Count,
            GeneratedPayloadBytes: generatedPayload.Count,
            DifferingByteCount: differingByteCount,
            FirstDiffs: samples.ToArray());
    }

    private static bool TryBuildTemplateCompatibleTopologySource(
        TopologyTraceSummary templateTrace,
        IReadOnlyList<uint> sourceIndices,
        out IReadOnlyList<uint> templateCompatibleIndices)
    {
        templateCompatibleIndices = [];
        if (templateTrace.UniqueTriangleIndices.Length == 0)
        {
            return false;
        }

        var templateCounts = CountTriangleSets(templateTrace.UniqueTriangleIndices);
        var sourceCounts = CountTriangleSets(sourceIndices);
        var templateVertexRange = BuildTriangleVertexRange(templateTrace.UniqueTriangleIndices);
        var sourceVertexRange = BuildTriangleVertexRange(sourceIndices);
        if (IsNearTemplateTriangleSet(templateCounts, sourceCounts))
        {
            templateCompatibleIndices = templateTrace.UniqueTriangleIndices;
            return true;
        }

        if (sourceIndices.Count <= templateTrace.UniqueTriangleIndices.Length)
        {
            return false;
        }

        if (templateVertexRange is null
            || sourceVertexRange is null
            || sourceVertexRange.UniqueCount <= templateVertexRange.UniqueCount * 4)
        {
            return false;
        }

        foreach (var (key, templateCount) in templateCounts)
        {
            if (!sourceCounts.TryGetValue(key, out var sourceCount) || sourceCount < templateCount)
            {
                return false;
            }
        }

        templateCompatibleIndices = templateTrace.UniqueTriangleIndices;
        return true;
    }

    private static bool IsNearTemplateTriangleSet(
        IReadOnlyDictionary<string, int> templateCounts,
        IReadOnlyDictionary<string, int> sourceCounts)
    {
        var templateOnly = CountOnly(templateCounts, sourceCounts);
        var sourceOnly = CountOnly(sourceCounts, templateCounts);
        var templateTotal = templateCounts.Values.Sum();
        var sourceTotal = sourceCounts.Values.Sum();

        return templateTotal > 0
            && Math.Abs(templateTotal - sourceTotal) <= 2
            && templateOnly <= 2
            && sourceOnly <= 1;
    }

    private static int CountOnly(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right)
    {
        var count = 0;
        foreach (var (key, leftCount) in left)
        {
            right.TryGetValue(key, out var rightCount);
            if (leftCount > rightCount)
            {
                count += leftCount - rightCount;
            }
        }

        return count;
    }

    private static string[] BuildOnlySamples(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right,
        int maxSamples = 16)
    {
        var samples = new List<string>(maxSamples);
        foreach (var (key, leftCount) in left.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            right.TryGetValue(key, out var rightCount);
            for (var i = rightCount; i < leftCount && samples.Count < maxSamples; i++)
            {
                samples.Add(key);
            }

            if (samples.Count >= maxSamples)
            {
                break;
            }
        }

        return samples.ToArray();
    }

    private static TopologyVertexRange? BuildTriangleVertexRange(IReadOnlyList<uint> indices)
    {
        if (indices.Count == 0)
        {
            return null;
        }

        return new TopologyVertexRange(indices.Min(), indices.Max(), indices.Distinct().Count());
    }

    private static bool HasSignificantZeroTopologyMarker(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.Equals(tokens[i].Kind, "zero", StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].VertexIndex > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountTemplateTopologyRestarts(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (IsNonPositiveTopologyToken(tokens[i]) && IsNonPositiveTopologyToken(tokens[i + 1]))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGeneratedTopologyRestarts(IReadOnlyList<byte> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (unchecked((sbyte)tokens[i]) <= 0 && unchecked((sbyte)tokens[i + 1]) <= 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountResolvedTopologyRestarts(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].IsNonPositive && tokens[i + 1].IsNonPositive)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetMaxIndex(IReadOnlyList<uint> indices)
    {
        var max = 0;
        foreach (var index in indices)
        {
            if (index > int.MaxValue)
            {
                throw new InvalidDataException("v1 importer supports mesh-local vertex indices up to 126.");
            }

            max = Math.Max(max, (int)index);
        }

        return max;
    }

    private static bool TopologyZeroMarkerPositionsMatch(
        ReadOnlySpan<byte> expectedPayload,
        ReadOnlySpan<byte> actualPayload,
        byte[]? expectedTexturePayload,
        byte[]? actualTexturePayload,
        int positionCount)
    {
        var expected = BuildResolvedTopologyTraceTokens(expectedPayload, expectedTexturePayload, positionCount);
        var actual = BuildResolvedTopologyTraceTokens(actualPayload, actualTexturePayload, positionCount);
        for (var i = 0; i < expected.Count; i++)
        {
            if (!expected[i].IsZeroMarker)
            {
                continue;
            }

            if (i >= actual.Count || !actual[i].IsZeroMarker)
            {
                return false;
            }
        }

        return true;
    }

    private static TopologyZeroMarkerSummary SummarizeTopologyZeroMarkers(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var indices = tokens
            .Select((token, index) => new { token, index })
            .Where(item => item.token.IsZeroMarker)
            .Select(item => item.index)
            .ToArray();

        return new TopologyZeroMarkerSummary(indices.Length, indices);
    }

    private static TopologyRowUsageSummary SummarizeGeneratedTopologyRowUsage(
        ReadOnlySpan<byte> payload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = BuildResolvedTopologyTraceTokens(payload, texturePayload, positionCount);
        var rows = tokens
            .Where(token => token.VertexIndex >= 0)
            .Select(token => token.VertexIndex)
            .ToArray();
        var uniqueRows = rows
            .Distinct()
            .Order()
            .ToArray();

        return new TopologyRowUsageSummary(
            tokens.Count,
            uniqueRows.Length == 0 ? null : uniqueRows[0],
            uniqueRows.Length == 0 ? null : uniqueRows[^1],
            uniqueRows.Length,
            tokens.Count(token => token.IsNonPositive),
            tokens.Count(token => token.IsZeroMarker),
            rows.Take(96).ToArray(),
            uniqueRows.Take(96).ToArray());
    }

    private static bool IsNonPositiveTopologyToken(ImportedTopologyPayloadToken token)
    {
        return token.Negative || string.Equals(token.Kind, "zero", StringComparison.Ordinal);
    }

    private static TopologyShapeSummary SummarizeTemplateTopologyShape(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var patterns = BuildTemplateTopologyTokenPatterns(tokens);
        return SummarizeTopologyShape(patterns);
    }

    private static TopologyShapeSummary SummarizeGeneratedTopologyShape(IReadOnlyList<byte> tokens)
    {
        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var isNegative = unchecked((sbyte)tokens[i]) < 0;
            var nextIsRestart = i + 1 < tokens.Count && unchecked((sbyte)tokens[i + 1]) <= 0;
            if (unchecked((sbyte)tokens[i]) <= 0 && nextIsRestart)
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

        return SummarizeTopologyShape(patterns.Where(pattern => pattern.Count > 0).ToList());
    }

    private static TopologyShapeSummary SummarizeResolvedTopologyShape(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var nextIsRestart = i + 1 < tokens.Count && tokens[i + 1].IsNonPositive;
            if (tokens[i].IsNonPositive && nextIsRestart)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(tokens[i].IsNonPositive);
        }

        return SummarizeTopologyShape(patterns.Where(pattern => pattern.Count > 0).ToList());
    }

    private static TopologyShapeSummary SummarizeTopologyShape(IReadOnlyList<List<bool>> patterns)
    {
        return new TopologyShapeSummary(
            patterns.Count,
            patterns.Sum(pattern => pattern.Count),
            patterns.Select(pattern => pattern.Count).ToArray(),
            patterns.Select(pattern => pattern.Count(value => value)).ToArray(),
            patterns.Select(pattern => pattern.Skip(2).Count(value => value)).ToArray());
    }

    private static bool TopologyShapesMatch(TopologyShapeSummary expected, TopologyShapeSummary actual)
    {
        return expected.SegmentCount == actual.SegmentCount
            && expected.EffectiveTokenCount == actual.EffectiveTokenCount
            && expected.SegmentTokenLengths.SequenceEqual(actual.SegmentTokenLengths)
            && expected.SegmentNegativeTokenCounts.SequenceEqual(actual.SegmentNegativeTokenCounts)
            && expected.SegmentMidStripNegativeTokenCounts.SequenceEqual(actual.SegmentMidStripNegativeTokenCounts);
    }

    private static TopologyTraceSummary TraceTemplateTopology(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var effectiveTokens = new List<TopologyTraceToken>();
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

        for (var i = 0; i < effectiveTokenCount; i++)
        {
            var token = tokens[i];
            effectiveTokens.Add(new TopologyTraceToken(
                token.Negative || string.Equals(token.Kind, "zero", StringComparison.Ordinal),
                token.VertexIndex,
                IsZeroMarker: false));
        }

        return TraceTopology(effectiveTokens);
    }

    private static TopologyTraceSummary TraceGeneratedTopology(IReadOnlyList<byte> tokens)
    {
        return TraceTopology(tokens
            .Select(token => new TopologyTraceToken(unchecked((sbyte)token) <= 0, (token & 0x7F) - 1, IsZeroMarker: false))
            .ToList());
    }

    private static TopologyTraceSummary TraceTopology(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var segments = new List<TopologyTraceSegment>();
        var allSeenTriangleKeys = new HashSet<string>(StringComparer.Ordinal);
        var allSeenTriangleIndices = new List<uint>();
        var currentStrip = new List<uint>();
        var currentEvents = new List<TopologyTraceControlEvent>();
        var segmentIndex = -1;
        var segmentTokenStart = 0;
        var segmentTokenCount = 0;
        var rawTriangleCount = 0;
        var uniqueTriangleCount = 0;
        var degenerateTriangleCount = 0;
        var duplicateTriangleCount = 0;
        var midStripControlCount = 0;

        void FlushSegment()
        {
            if (segmentIndex < 0)
            {
                return;
            }

            segments.Add(new TopologyTraceSegment(
                segmentIndex,
                segmentTokenStart,
                segmentTokenCount,
                rawTriangleCount,
                uniqueTriangleCount,
                degenerateTriangleCount,
                duplicateTriangleCount,
                midStripControlCount,
                currentEvents.ToArray()));
        }

        void StartSegment(int tokenIndex)
        {
            FlushSegment();
            segmentIndex++;
            segmentTokenStart = tokenIndex;
            segmentTokenCount = 0;
            rawTriangleCount = 0;
            uniqueTriangleCount = 0;
            degenerateTriangleCount = 0;
            duplicateTriangleCount = 0;
            midStripControlCount = 0;
            currentEvents = [];
            currentStrip = [];
        }

        TopologyTraceEmittedTriangle? TryEmitTriangle()
        {
            if (currentStrip.Count < 3)
            {
                return null;
            }

            var k = currentStrip.Count - 1;
            var a = currentStrip[k - 2];
            var b = currentStrip[k - 1];
            var c = currentStrip[k];
            var flip = ((k - 2) & 1) != 0;
            var i0 = a;
            var i1 = flip ? c : b;
            var i2 = flip ? b : c;
            var isDegenerate = i0 == i1 || i1 == i2 || i0 == i2;
            var key = BuildIndexTriangleKey(i0, i1, i2);
            var isDuplicate = !isDegenerate && !allSeenTriangleKeys.Add(key);

            rawTriangleCount++;
            if (isDegenerate)
            {
                degenerateTriangleCount++;
            }
            else if (isDuplicate)
            {
                duplicateTriangleCount++;
            }
            else
            {
                uniqueTriangleCount++;
                allSeenTriangleIndices.Add(i0);
                allSeenTriangleIndices.Add(i1);
                allSeenTriangleIndices.Add(i2);
            }

            return new TopologyTraceEmittedTriangle(
                new[] { i0, i1, i2 },
                flip,
                isDegenerate,
                isDuplicate,
                isDegenerate || isDuplicate ? null : uniqueTriangleCount - 1);
        }

        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var nextIsRestart = tokenIndex + 1 < tokens.Count && tokens[tokenIndex + 1].IsNonPositive;
            if (segmentIndex < 0 || (token.IsNonPositive && nextIsRestart))
            {
                StartSegment(tokenIndex);
            }

            segmentTokenCount++;
            var emitted = new List<TopologyTraceEmittedTriangle>();
            var action = "append";
            if (token.IsNonPositive && !nextIsRestart)
            {
                action = "duplicate-previous";
                if (segmentTokenCount > 2)
                {
                    midStripControlCount++;
                }

                if (currentStrip.Count > 0)
                {
                    currentStrip.Add(currentStrip[^1]);
                    var duplicateTriangle = TryEmitTriangle();
                    if (duplicateTriangle is not null)
                    {
                        emitted.Add(duplicateTriangle);
                    }
                }
            }
            else if (token.IsNonPositive)
            {
                action = "restart";
            }

            if (token.VertexIndex >= 0)
            {
                currentStrip.Add((uint)token.VertexIndex);
                var triangle = TryEmitTriangle();
                if (triangle is not null)
                {
                    emitted.Add(triangle);
                }
            }

            if (action != "append" || emitted.Any(triangle => triangle.IsDegenerate || triangle.IsDuplicate))
            {
                currentEvents.Add(new TopologyTraceControlEvent(
                    tokenIndex,
                    segmentIndex,
                    segmentTokenCount - 1,
                    token.IsNonPositive,
                    token.VertexIndex,
                    action,
                    currentStrip.Count,
                    emitted.ToArray()));
            }
        }

        FlushSegment();
        return new TopologyTraceSummary(
            segments.Count,
            segments.Sum(segment => segment.RawTriangleCount),
            segments.Sum(segment => segment.UniqueTriangleCount),
            segments.Sum(segment => segment.DegenerateTriangleCount),
            segments.Sum(segment => segment.DuplicateTriangleCount),
            segments.Sum(segment => segment.MidStripControlCount),
            segments.ToArray(),
            allSeenTriangleIndices.ToArray());
    }

    private static Dictionary<string, int> CountTriangleSets(IReadOnlyList<uint> indices)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var key = BuildIndexTriangleKey(indices[i], indices[i + 1], indices[i + 2]);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        return counts;
    }

}
