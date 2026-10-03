using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfPositionBuilder
{
    public static List<Vector3> BuildPositions(TieClass tie, TieLodTopology topology)
        => BuildPs2Positions(tie, topology)
            .Select(position => GltfCoordinateBasis.FromPs2Position(position.X, position.Y, position.Z))
            .ToList();

    public static List<Vector3> BuildPs2Positions(TieClass tie, TieLodTopology topology)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);

        var positions = new List<Vector3>(topology.LogicalVertices.Count);
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (vertex.DecodedVertex is null && vertex.VertexRow is null && vertex.AddressRow is null)
            {
                throw new InvalidDataException(
                    $"Tie LOD {topology.LodIndex} logical vertex {vertex.LogicalVertexIndex} has no decoded vertex.");
            }

            positions.Add(ToPs2Position(tie, vertex));
        }

        return positions;
    }

    private static Vector3 ToPs2Position(TieClass tie, TieLogicalVertex vertex)
    {
        if (vertex.DecodedVertex is { } decodedVertex)
        {
            return ToPs2Position(tie, decodedVertex.X, decodedVertex.Y, decodedVertex.Z);
        }

        var row = vertex.VertexRow ?? vertex.AddressRow!;
        if (TiePacketVertexRowClassifier.TrySelectPositionSlot(row, out var slot)
            && slot == TiePacketVertexPositionSlot.Second)
        {
            return ToPs2Position(tie, row.Data0, row.Data1, row.Data2);
        }

        return ToPs2Position(tie, row.X, row.Y, row.Z);
    }

    private static Vector3 ToPs2Position(TieClass tie, short sourceX, short sourceY, short sourceZ)
    {
        var scale = tie.Header.Scale / 1024f;
        return new(sourceX * scale, sourceY * scale, sourceZ * scale);
    }
}
