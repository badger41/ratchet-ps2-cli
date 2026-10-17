using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static bool TryBuildPatternedStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> tokenPattern,
        IReadOnlySet<string> allowedTriangleKeys,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return tokenPattern.Count == 0;
        }

        var sourceVertices = triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var memo = new HashSet<string>(StringComparer.Ordinal);
        var searchBudget = 250000;
        return TrySolvePatternedStrip(
            triangles,
            tokenPattern,
            sourceVertices,
            allowedTriangleKeys,
            tokenIndex: 0,
            strip: [],
            triangleIndex: 0,
            tokens,
            memo,
            ref searchBudget);
    }

    private static bool TrySolvePatternedStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> tokenPattern,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlySet<string> allowedTriangleKeys,
        int tokenIndex,
        List<uint> strip,
        int triangleIndex,
        List<byte> tokens,
        HashSet<string> memo,
        ref int searchBudget)
    {
        if (searchBudget-- <= 0)
        {
            return false;
        }

        if (tokenIndex == tokenPattern.Count)
        {
            return triangleIndex == triangles.Count;
        }

        var lastA = strip.Count >= 2 ? strip[^2] : 127u;
        var lastB = strip.Count >= 1 ? strip[^1] : 127u;
        var memoKey = $"{tokenIndex}|{strip.Count}|{lastA}|{lastB}|{triangleIndex}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var isNegative = tokenPattern[tokenIndex];
        var nextIsRestart = isNegative
            && tokenIndex + 1 < tokenPattern.Count
            && tokenPattern[tokenIndex + 1];
        foreach (var candidate in GetPrioritizedPatternCandidates(triangles, triangleIndex, sourceVertices))
        {
            var nextStrip = new List<uint>(strip);
            var nextTriangleIndex = triangleIndex;
            if (isNegative && !nextIsRestart)
            {
                if (nextStrip.Count == 0)
                {
                    continue;
                }

                nextStrip.Add(nextStrip[^1]);
                if (!TryValidateNewStripTriangle(nextStrip, triangles, allowedTriangleKeys, ref nextTriangleIndex))
                {
                    continue;
                }
            }

            nextStrip.Add(candidate);
            if (!TryValidateNewStripTriangle(nextStrip, triangles, allowedTriangleKeys, ref nextTriangleIndex))
            {
                continue;
            }

            tokens.Add(Ps2VifTopology.EncodeIndexToken(candidate, isNegative));
            if (TrySolvePatternedStrip(
                triangles,
                tokenPattern,
                sourceVertices,
                allowedTriangleKeys,
                tokenIndex + 1,
                nextStrip,
                nextTriangleIndex,
                tokens,
                memo,
                ref searchBudget))
            {
                return true;
            }

            tokens.RemoveAt(tokens.Count - 1);
        }

        return false;
    }

    private static IEnumerable<uint> GetPrioritizedPatternCandidates(
        IReadOnlyList<TopologyTriangle> triangles,
        int triangleIndex,
        IReadOnlyList<uint> sourceVertices)
    {
        var yielded = new HashSet<uint>();
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

        foreach (var vertex in sourceVertices)
        {
            if (yielded.Add(vertex))
            {
                yield return vertex;
            }
        }
    }

    private static bool TryValidateNewStripTriangle(
        IReadOnlyList<uint> strip,
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlySet<string> allowedTriangleKeys,
        ref int triangleIndex)
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
            return true;
        }

        if (triangleIndex >= triangles.Count)
        {
            return allowedTriangleKeys.Contains(BuildIndexTriangleKey(i0, i1, i2));
        }

        var expected = triangles[triangleIndex];
        if (expected.A == i0 && expected.B == i1 && expected.C == i2)
        {
            triangleIndex++;
            return true;
        }

        return allowedTriangleKeys.Contains(BuildIndexTriangleKey(i0, i1, i2));
    }

    private static bool TryBuildSequentialStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return true;
        }

        for (var seedRotation = 0; seedRotation < 3; seedRotation++)
        {
            var seed = Ps2VifTopology.RotateTriangle(triangles[0], seedRotation);
            var currentStrip = new List<uint> { seed.A, seed.A, seed.C, seed.B };
            var candidateTokens = new List<byte>
            {
                Ps2VifTopology.EncodeIndexToken(seed.A, negative: true),
                Ps2VifTopology.EncodeIndexToken(seed.C, negative: true),
                Ps2VifTopology.EncodeIndexToken(seed.B, negative: false)
            };

            var matched = true;
            for (var i = 1; i < triangles.Count; i++)
            {
                if (!Ps2VifTopology.TryFindAppendableTriangleRotation(currentStrip, triangles[i], out var rotated, out var nextVertex))
                {
                    matched = false;
                    break;
                }

                candidateTokens.Add(Ps2VifTopology.EncodeIndexToken(nextVertex, negative: false));
                currentStrip.Add(nextVertex);
            }

            if (!matched)
            {
                continue;
            }

            tokens = candidateTokens;
            return true;
        }

        return TryBuildSingleGreedyStripTokens(triangles, out tokens);
    }

    private static bool TryBuildSingleGreedyStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return true;
        }

        if (Ps2VifTopology.TryBuildSingleRestartStrip(triangles, out var build))
        {
            tokens = build!.Tokens;
            return true;
        }

        return false;
    }

}
