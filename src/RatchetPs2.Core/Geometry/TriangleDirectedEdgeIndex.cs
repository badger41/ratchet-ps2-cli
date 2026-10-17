namespace RatchetPs2.Core.Geometry;

internal readonly record struct TriangleEdgeContinuation(int TriangleIndex, uint NextVertex);

internal sealed class TriangleDirectedEdgeIndex
{
    private static readonly IReadOnlyList<TriangleEdgeContinuation> Empty = [];
    private readonly Dictionary<(uint From, uint To), List<TriangleEdgeContinuation>> _edges = [];

    public TriangleDirectedEdgeIndex(
        IReadOnlyList<uint> triangleIndices,
        CancellationToken cancellationToken = default)
    {
        TriangleIndexUtils.ValidateTriangleIndexList(triangleIndices, "Mesh indices");
        for (var triangle = 0; triangle < triangleIndices.Count / 3; triangle++)
        {
            if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = triangle * 3;
            Add(triangleIndices[offset], triangleIndices[offset + 1], triangleIndices[offset + 2], triangle);
            Add(triangleIndices[offset + 1], triangleIndices[offset + 2], triangleIndices[offset], triangle);
            Add(triangleIndices[offset + 2], triangleIndices[offset], triangleIndices[offset + 1], triangle);
        }
    }

    public IReadOnlyList<TriangleEdgeContinuation> Get(uint from, uint to) =>
        _edges.TryGetValue((from, to), out var continuations) ? continuations : Empty;

    public bool HasConnectionsBetweenTriangles()
    {
        foreach (var (edge, continuations) in _edges)
        {
            var reversed = Get(edge.To, edge.From);
            if (reversed.Count == 0) continue;
            var triangleIndex = continuations[0].TriangleIndex;
            if (continuations.Any(value => value.TriangleIndex != triangleIndex)
                || reversed.Any(value => value.TriangleIndex != triangleIndex))
                return true;
        }
        return false;
    }

    private void Add(uint from, uint to, uint nextVertex, int triangleIndex)
    {
        if (!_edges.TryGetValue((from, to), out var continuations))
            _edges.Add((from, to), continuations = []);
        if (continuations.Count == 0 || continuations[^1].TriangleIndex != triangleIndex)
            continuations.Add(new(triangleIndex, nextVertex));
    }
}
