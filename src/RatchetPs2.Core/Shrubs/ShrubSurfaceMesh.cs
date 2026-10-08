using System.Numerics;

namespace RatchetPs2.Core.Shrubs;

public sealed record ShrubTriangle(int A, int B, int C);

public sealed record ShrubSurfaceMesh(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<ShrubTriangle> Triangles);

public static class ShrubSurfaceMeshExtractor
{
    public static ShrubSurfaceMesh Extract(ShrubClass shrub)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        var positions = new List<Vector3>();
        var triangles = new List<ShrubTriangle>();
        var scale = shrub.Header.Scale / 1024f;
        foreach (var primitive in shrub.Packets.SelectMany(packet => packet.Primitives)
            .OfType<ShrubVertexPrimitive>())
        {
            var offset = positions.Count;
            positions.AddRange(primitive.Vertices.Select(vertex => new Vector3(
                vertex.X * scale, vertex.Y * scale, vertex.Z * scale)));
            if (primitive.GeometryType == ShrubGeometryType.TriangleList)
            {
                for (var index = 0; index + 2 < primitive.Vertices.Count; index += 3)
                    Add(primitive, offset, index, index + 1, index + 2);
            }
            else
            {
                for (var index = 0; index + 2 < primitive.Vertices.Count; index++)
                    Add(primitive, offset, index, index + 1, index + 2);
            }
        }
        return new(positions, triangles);

        void Add(ShrubVertexPrimitive primitive, int offset, int localA, int localB, int localC)
        {
            var a = offset + localA;
            var b = offset + localB;
            var c = offset + localC;
            var faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (faceNormal.LengthSquared() <= 0) return;
            var normal = Normal(primitive.Vertices[localA].NormalIndex)
                + Normal(primitive.Vertices[localB].NormalIndex)
                + Normal(primitive.Vertices[localC].NormalIndex);
            if (normal.LengthSquared() > 0 && Vector3.Dot(faceNormal, normal) < 0) (b, c) = (c, b);
            triangles.Add(new(a, b, c));
        }

        Vector3 Normal(int index) => (uint)index < shrub.Normals.Count
            ? new(shrub.Normals[index].X, shrub.Normals[index].Y, shrub.Normals[index].Z)
            : Vector3.Zero;
    }
}
