using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Games.UYA.Geometry;

internal static class UyaTriangleStripPlanner
{
    public const int MaximumStripVertices = 24;

    public static IReadOnlyList<UyaTriangleStrip> Build(
        uint[] indices,
        CancellationToken cancellationToken,
        int maximumStripVertices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new InvalidDataException("UYA triangle strip input must contain complete triangles.");
        if (maximumStripVertices is < 4 or > MaximumStripVertices || maximumStripVertices % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStripVertices));
        var triangleCount = indices.Length / 3;
        var consumed = new bool[triangleCount];
        var edges = new TriangleDirectedEdgeIndex(indices, cancellationToken);
        if (!edges.HasConnectionsBetweenTriangles())
            return BuildIsolatedStrips(indices, cancellationToken);
        var builtStrips = new List<UyaTriangleStrip>();
        var remaining = triangleCount;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<UyaStripVertex>? strip = null;
            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (consumed[triangle]) continue;
                var candidate = BuildStrip(indices, triangle, consumed, edges, maximumStripVertices);
                if (strip is null || candidate.Count > strip.Count)
                    strip = candidate;
                if (strip.Count == maximumStripVertices) break;
            }
            var selected = strip ?? throw new InvalidDataException("UYA strip construction left source triangles unhandled.");
            var sourceTriangles = selected.Select(value => value.SourceTriangleIndex).Distinct().ToArray();
            foreach (var sourceTriangle in sourceTriangles)
                consumed[sourceTriangle] = true;
            remaining -= sourceTriangles.Length;
            // The UYA VU loop consumes strip vertices in pairs; retail pads odd strips with a degenerate tail.
            if (selected.Count % 2 != 0)
                selected = selected.Append(selected[^1]).ToArray();
            builtStrips.Add(new(selected));
        }

        return builtStrips;
    }

    private static IReadOnlyList<UyaTriangleStrip> BuildIsolatedStrips(
        uint[] indices,
        CancellationToken cancellationToken)
    {
        var strips = new UyaTriangleStrip[indices.Length / 3];
        for (var triangle = 0; triangle < strips.Length; triangle++)
        {
            if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = triangle * 3;
            strips[triangle] = new(new UyaStripVertex[]
            {
                new(indices[offset], triangle),
                new(indices[offset + 2], triangle),
                new(indices[offset + 1], triangle),
                new(indices[offset + 1], triangle),
            });
        }
        return strips;
    }

    private static IReadOnlyList<UyaStripVertex> BuildStrip(
        uint[] indices,
        int triangle,
        bool[] consumed,
        TriangleDirectedEdgeIndex edges,
        int maximumStripVertices)
    {
        var offset = triangle * 3;
        UyaStripVertex[]? longest = null;
        Span<UyaStripVertex> vertices = stackalloc UyaStripVertex[MaximumStripVertices];
        for (var rotation = 0; rotation < 3; rotation++)
        {
            var a = indices[offset + rotation];
            var b = indices[offset + (rotation + 1) % 3];
            var c = indices[offset + (rotation + 2) % 3];
            vertices[0] = new(a, triangle);
            vertices[1] = new(c, triangle);
            vertices[2] = new(b, triangle);
            var vertexCount = 3;
            while (vertexCount < maximumStripVertices)
            {
                var left = vertices[vertexCount - 2].SourceVertexIndex;
                var right = vertices[vertexCount - 1].SourceVertexIndex;
                var edge = vertexCount % 2 == 0 ? (right, left) : (left, right);
                TriangleEdgeContinuation? continuation = null;
                foreach (var value in edges.Get(edge.Item1, edge.Item2))
                {
                    if (consumed[value.TriangleIndex]
                        || ContainsTriangle(vertices, vertexCount, value.TriangleIndex)) continue;
                    continuation = value;
                    break;
                }
                if (continuation is not { } next) break;
                vertices[vertexCount++] = new(next.NextVertex, next.TriangleIndex);
            }
            if (longest is null || vertexCount > longest.Length)
                longest = vertices[..vertexCount].ToArray();
        }

        return longest!;
    }

    private static bool ContainsTriangle(ReadOnlySpan<UyaStripVertex> vertices, int count, int triangle)
    {
        for (var index = 0; index < count; index++)
        {
            if (vertices[index].SourceTriangleIndex == triangle) return true;
        }
        return false;
    }
}

internal sealed record UyaTriangleStrip(IReadOnlyList<UyaStripVertex> Vertices);

internal readonly record struct UyaStripVertex(uint SourceVertexIndex, int SourceTriangleIndex);
