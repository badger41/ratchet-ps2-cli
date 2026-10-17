using RatchetPs2.Core.IO.Vif;
using System.Diagnostics;

internal static class Ps2VifTopologyTests
{
    public static void Run()
    {
        var random = new Random(0x5A17);
        for (var test = 0; test < 100; test++)
        {
            var vertexCount = random.Next(3, 24);
            var triangleCount = random.Next(1, 18);
            var indices = new List<uint>(triangleCount * 3);
            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                var a = random.Next(vertexCount);
                var b = random.Next(vertexCount - 1);
                if (b >= a) b++;
                var c = random.Next(vertexCount - 2);
                if (c >= Math.Min(a, b)) c++;
                if (c >= Math.Max(a, b)) c++;
                indices.Add((uint)a);
                indices.Add((uint)b);
                indices.Add((uint)c);
            }

            var expected = BuildLegacyRestartStripTokens(indices);
            var actual = Ps2VifTopology.BuildRestartStripTokens(indices);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"Indexed VIF strip construction changed output for randomized case {test}.");
        }

        var dense = BuildGrid(8, 8);
        var stopwatch = Stopwatch.StartNew();
        var denseExpected = BuildLegacyRestartStripTokens(dense);
        var legacyMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();
        var denseActual = Ps2VifTopology.BuildRestartStripTokens(dense);
        var indexedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        if (!denseActual.SequenceEqual(denseExpected))
            throw new InvalidOperationException("Indexed VIF strip construction changed dense-grid output.");

        Console.WriteLine(
            $"Indexed PS2 VIF topology tests passed (128 triangles: {legacyMilliseconds:F1} ms legacy, "
            + $"{indexedMilliseconds:F1} ms indexed).");
    }

    private static List<uint> BuildGrid(int width, int height)
    {
        var indices = new List<uint>(width * height * 6);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = checked((uint)(y * (width + 1) + x));
                var b = a + 1;
                var c = checked((uint)((y + 1) * (width + 1) + x));
                var d = c + 1;
                indices.AddRange([a, c, b, b, c, d]);
            }
        }
        return indices;
    }

    private static List<byte> BuildLegacyRestartStripTokens(IReadOnlyList<uint> triangleIndices)
    {
        var triangles = new List<TopologyTriangle>(triangleIndices.Count / 3);
        for (var index = 0; index < triangleIndices.Count; index += 3)
            triangles.Add(new(triangleIndices[index], triangleIndices[index + 1], triangleIndices[index + 2]));

        var available = Enumerable.Repeat(true, triangles.Count).ToArray();
        var remaining = triangles.Count;
        var tokens = new List<byte>(triangleIndices.Count);
        while (remaining > 0)
        {
            Ps2VifTopologyStripBuild? best = null;
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                if (!available[triangleIndex]) continue;
                for (var rotation = 0; rotation < 3; rotation++)
                {
                    var candidate = BuildLegacyGreedyStrip(triangles, available, triangleIndex, rotation);
                    if (best is null
                        || candidate.TriangleIndices.Count > best.TriangleIndices.Count
                        || (candidate.TriangleIndices.Count == best.TriangleIndices.Count
                            && candidate.Tokens.Count < best.Tokens.Count))
                        best = candidate;
                }
            }

            if (best is null) break;
            tokens.AddRange(best.Tokens);
            foreach (var triangleIndex in best.TriangleIndices)
            {
                if (!available[triangleIndex]) continue;
                available[triangleIndex] = false;
                remaining--;
            }
        }

        return tokens;
    }

    private static Ps2VifTopologyStripBuild BuildLegacyGreedyStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> available,
        int seedTriangleIndex,
        int seedRotation)
    {
        var localAvailable = available.ToArray();
        localAvailable[seedTriangleIndex] = false;
        var seed = Ps2VifTopology.RotateTriangle(triangles[seedTriangleIndex], seedRotation);
        var currentStrip = new List<uint> { seed.A, seed.A, seed.C, seed.B };
        var tokens = new List<byte>
        {
            Ps2VifTopology.EncodeIndexToken(seed.A, negative: true),
            Ps2VifTopology.EncodeIndexToken(seed.C, negative: true),
            Ps2VifTopology.EncodeIndexToken(seed.B, negative: false),
        };
        var usedTriangleIndices = new List<int> { seedTriangleIndex };

        var found = true;
        while (found)
        {
            found = false;
            var bestTriangleIndex = -1;
            uint bestNextVertex = 0;
            var bestContinuationCount = -1;
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                if (!localAvailable[triangleIndex]
                    || !Ps2VifTopology.TryFindAppendableTriangleRotation(
                        currentStrip, triangles[triangleIndex], out _, out var nextVertex))
                    continue;
                var continuationStrip = new List<uint>(currentStrip) { nextVertex };
                var continuationCount = CountLegacyAppendableTriangles(
                    continuationStrip, triangles, localAvailable, triangleIndex);
                if (continuationCount <= bestContinuationCount) continue;
                bestTriangleIndex = triangleIndex;
                bestNextVertex = nextVertex;
                bestContinuationCount = continuationCount;
                found = true;
            }

            if (bestTriangleIndex < 0) continue;
            tokens.Add(Ps2VifTopology.EncodeIndexToken(bestNextVertex, negative: false));
            currentStrip.Add(bestNextVertex);
            localAvailable[bestTriangleIndex] = false;
            usedTriangleIndices.Add(bestTriangleIndex);
        }

        return new(tokens, usedTriangleIndices);
    }

    private static int CountLegacyAppendableTriangles(
        IReadOnlyList<uint> currentStrip,
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> available,
        int excludingTriangleIndex)
    {
        var count = 0;
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            if (triangleIndex == excludingTriangleIndex || !available[triangleIndex]) continue;
            if (Ps2VifTopology.TryFindAppendableTriangleRotation(
                currentStrip, triangles[triangleIndex], out _, out _)) count++;
        }
        return count;
    }
}
