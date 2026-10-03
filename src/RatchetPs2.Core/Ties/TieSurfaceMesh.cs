using System.Numerics;

namespace RatchetPs2.Core.Ties;

public sealed record TieSurfaceMesh(
    int LodIndex,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<TieTriangle> Triangles);

public static class TieSurfaceMeshExtractor
{
    public static TieSurfaceMesh Extract(TieClass tie, int lodIndex)
    {
        ArgumentNullException.ThrowIfNull(tie);
        var topology = tie.LodTopologies.FirstOrDefault(value => value.LodIndex == lodIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(lodIndex), $"Tie LOD {lodIndex} was not decoded.");
        if (topology.UnresolvedLogicalVertexCount != 0)
        {
            throw new InvalidDataException(
                $"Tie LOD {lodIndex} has {topology.UnresolvedLogicalVertexCount} unresolved logical vertices.");
        }

        var positions = TieGltfPositionBuilder.BuildPs2Positions(tie, topology);
        foreach (var triangle in topology.Triangles)
        {
            if ((uint)triangle.A >= positions.Count
                || (uint)triangle.B >= positions.Count
                || (uint)triangle.C >= positions.Count)
            {
                throw new InvalidDataException($"Tie LOD {lodIndex} contains an invalid triangle vertex index.");
            }
        }

        return new(lodIndex, positions, topology.Triangles);
    }
}
