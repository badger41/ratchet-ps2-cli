using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.RC1.Level;

internal readonly record struct Rc1TieLightingRecipe(int A, int B, int C, int D)
{
    public int Kind => A == D ? 0 : A == B && C == D ? 1 : 4;
    public int Size => Kind == 4 ? 8 : 4;
    public static Rc1TieLightingRecipe Direct(int index) => new(index, index, index, index);

    public static Rc1TieLightingRecipe Average(Rc1TieLightingRecipe left, Rc1TieLightingRecipe right)
    {
        // There are only three LODs, so each input has at most two equally weighted sources.
        if (left.A != left.B || left.C != left.D || right.A != right.B || right.C != right.D)
            throw new InvalidDataException("RC1 lighting morph exceeds four native color sources.");
        var sources = new[] { left.A, left.C, right.A, right.C };
        Array.Sort(sources);
        return new(sources[0], sources[1], sources[2], sources[3]);
    }
}

// UYA morphs positions without RC1's RGB blend; carry coarse endpoint lighting through the finer LODs.
internal static class Rc1TieLodLighting
{
    public static Dictionary<(int Lod, int Packet, int Row), Rc1TieLightingRecipe> Build(
        byte[] source, TieClass tie, CancellationToken token, bool previousLighting)
    {
        var remaps = tie.VertexNormalRemaps.ToDictionary(value =>
            (value.LodIndex, value.PacketIndex, value.VertexRowIndex));
        var result = new Dictionary<(int, int, int), Rc1TieLightingRecipe>();
        var nextVertices = new Dictionary<(Vector3 Position, int Normal), Rc1TieLightingRecipe>();
        var nextEdges = new Dictionary<(int X, int Y, int Z, int A, int B), List<Edge>>();
        for (var lod = 2; lod >= 0; lod--)
        {
            var blocks = tie.PacketDataBlocks.Where(block => block.LodIndex == lod).ToArray();
            if (tie.LodTopologies[lod].Triangles.Count == 0) continue;
            foreach (var block in blocks)
                foreach (var vertex in block.DecodedVertices)
                {
                    token.ThrowIfCancellationRequested();
                    var key = (lod, block.PacketIndex, vertex.SourceRowIndex);
                    if (!remaps.TryGetValue(key, out var remap))
                        throw new InvalidDataException($"RC1 LOD {lod} vertex has no lighting index.");
                    var position = Position(vertex);
                    var recipe = Rc1TieLightingRecipe.Direct(remap.NormalIndex);
                    if (!previousLighting && nextVertices.Count > 0)
                    {
                        if (vertex.Kind == TiePacketDecodedVertexKind.Dinky)
                            recipe = nextVertices.GetValueOrDefault((position, remap.NormalIndex), recipe);
                        else
                        {
                            var a = source[remap.Offset + 1] & 63;
                            var b = source[remap.Offset + 2] & 63;
                            if (a >= tie.VertexNormals.Count || b >= tie.VertexNormals.Count)
                                throw new InvalidDataException("RC1 morph lighting index exceeds its normal palette.");
                            var target = position + new Vector3(
                                BinaryPrimitives.ReadInt16LittleEndian(vertex.Bytes),
                                BinaryPrimitives.ReadInt16LittleEndian(vertex.Bytes.AsSpan(2)),
                                BinaryPrimitives.ReadInt16LittleEndian(vertex.Bytes.AsSpan(4)));
                            recipe = FindEdge(target, a, b, nextEdges, token)
                                ?? Rc1TieLightingRecipe.Average(Rc1TieLightingRecipe.Direct(a), Rc1TieLightingRecipe.Direct(b));
                        }
                    }
                    result.Add(key, recipe);
                }
            nextVertices.Clear();
            nextEdges.Clear();
            foreach (var block in blocks)
                foreach (var vertex in block.DecodedVertices)
                {
                    var key = (lod, block.PacketIndex, vertex.SourceRowIndex);
                    nextVertices.TryAdd((Position(vertex), remaps[key].NormalIndex), result[key]);
                }
            var topology = tie.LodTopologies[lod];
            var seen = new HashSet<Edge>();
            foreach (var triangle in topology.Triangles)
            {
                token.ThrowIfCancellationRequested();
                AddEdge(triangle.A, triangle.B);
                AddEdge(triangle.B, triangle.C);
                AddEdge(triangle.C, triangle.A);
            }

            void AddEdge(int first, int second)
            {
                // LOD0 has no finer consumer; its recipes can already contain four sources.
                if (lod == 0) return;
                var left = topology.LogicalVertices[first];
                var right = topology.LogicalVertices[second];
                var a = (lod, left.PacketIndex, left.DecodedVertex!.SourceRowIndex);
                var b = (lod, right.PacketIndex, right.DecodedVertex!.SourceRowIndex);
                var p = Position(left.DecodedVertex);
                var q = Position(right.DecodedVertex);
                if (p == q) return;
                var edge = new Edge((p + q) * .5f, Math.Min(remaps[a].NormalIndex, remaps[b].NormalIndex),
                    Math.Max(remaps[a].NormalIndex, remaps[b].NormalIndex),
                    Rc1TieLightingRecipe.Average(result[a], result[b]));
                if (!seen.Add(edge)) return;
                var cell = Cell(edge.Position, edge.A, edge.B);
                if (!nextEdges.TryGetValue(cell, out var list)) nextEdges[cell] = list = [];
                list.Add(edge);
            }
        }
        return result;
    }

    private static Rc1TieLightingRecipe? FindEdge(Vector3 position, int a, int b,
        Dictionary<(int X, int Y, int Z, int A, int B), List<Edge>> edges, CancellationToken token)
    {
        var cell = Cell(position, Math.Min(a, b), Math.Max(a, b));
        // RC1 rounds the morph delta and the destination midpoint independently to signed shorts.
        var distance = 9f;
        Rc1TieLightingRecipe? recipe = null;
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                {
                    if (!edges.TryGetValue((cell.X + x, cell.Y + y, cell.Z + z, cell.A, cell.B), out var candidates)) continue;
                    foreach (var edge in candidates)
                    {
                        token.ThrowIfCancellationRequested();
                        var candidateDistance = Vector3.DistanceSquared(position, edge.Position);
                        if (candidateDistance >= distance) continue;
                        distance = candidateDistance;
                        recipe = edge.Recipe;
                    }
                }
        return recipe;
    }

    private static Vector3 Position(TiePacketDecodedVertex vertex) => new(vertex.X, vertex.Y, vertex.Z);
    private static (int X, int Y, int Z, int A, int B) Cell(Vector3 p, int a, int b) =>
        ((int)MathF.Floor(p.X / 4), (int)MathF.Floor(p.Y / 4), (int)MathF.Floor(p.Z / 4), a, b);
    private readonly record struct Edge(Vector3 Position, int A, int B, Rc1TieLightingRecipe Recipe);
}
