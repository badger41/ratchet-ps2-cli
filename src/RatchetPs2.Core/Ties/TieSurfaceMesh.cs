using System.Numerics;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Ties;

public sealed record TieSurfaceMesh(
    int LodIndex,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<TieTriangle> Triangles);

public static class TieSurfaceMeshExtractor
{
    public static IReadOnlyList<TexturedSurfaceTriangle> ExtractTextured(TieClass tie, int lodIndex)
    {
        var mesh = Extract(tie, lodIndex);
        var topology = tie.LodTopologies.Single(value => value.LodIndex == lodIndex);
        var uv = TieGltfTexCoordBuilder.BuildTexCoords(tie, topology);
        var packets = tie.PacketTables.FirstOrDefault(table => table.LodIndex == lodIndex)?.Packets
            .ToDictionary(packet => packet.PacketIndex) ?? [];
        return mesh.Triangles.Select(triangle =>
        {
            var strip = topology.Strips[triangle.StripIndex];
            packets.TryGetValue(strip.PacketIndex, out var packet);
            var material = TieGltfPacketIndexGroupBuilder.SelectShaderIndex(packet, strip);
            if (material >= tie.Shaders.Count) throw new InvalidDataException("Tie surface references a missing shader.");
            var shader = material < 0 ? null : tie.Shaders[material];
            Vector2 Uv(int index)
            {
                var value = uv[index];
                // Same signed Q12 interpretation used by the existing material exporter.
                return new(shader?.ClampU == true ? value.X - MathF.Floor((value.X + 8) / 16) * 16 : value.X,
                    shader?.ClampV == true ? value.Y - MathF.Floor((value.Y + 8) / 16) * 16 : value.Y);
            }
            return new TexturedSurfaceTriangle(mesh.Positions[triangle.A], mesh.Positions[triangle.B], mesh.Positions[triangle.C],
                Uv(triangle.A), Uv(triangle.B), Uv(triangle.C), material, shader?.ClampU == true, shader?.ClampV == true);
        }).ToArray();
    }

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
