using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    internal static TfragPieceGeometry DecodeCanonicalPiece(
        TfragTerrain terrain,
        TfragChunk chunk,
        CancellationToken cancellationToken)
    {
        var options = new TfragGltfExportOptions();
        var center = GltfCoordinateBasis.FromPs2Position(
            chunk.BoundingSphere.X * options.WorldPositionScale,
            chunk.BoundingSphere.Y * options.WorldPositionScale,
            chunk.BoundingSphere.Z * options.WorldPositionScale);
        var lods = new TfragPieceLod[3];
        for (var lodIndex = 0; lodIndex < lods.Length; lodIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mesh = DecodeChunkLod(terrain, chunk, lodIndex, options).Mesh;
            lods[lodIndex] = new TfragPieceLod(
                lodIndex,
                mesh?.Groups.Select(group => ConvertPrimitive(group, center)).ToArray()
                    ?? []);
        }

        var positions = lods
            .SelectMany(lod => lod.Primitives)
            .SelectMany(primitive => primitive.Vertices)
            .Select(vertex => vertex.Position)
            .ToArray();
        var bounds = BuildBounds(positions, chunk.BoundingSphere.Radius * options.WorldPositionScale);
        var textureIds = lods
            .SelectMany(lod => lod.Primitives)
            .Select(primitive => primitive.Material.TextureId)
            .Where(textureId => textureId >= 0)
            .Distinct()
            .Order()
            .ToArray();
        return new TfragPieceGeometry(bounds, textureIds, lods);
    }

    private static TfragPiecePrimitive ConvertPrimitive(TfragPrimitiveGroup group, Vector3 center)
    {
        var vertexCount = group.Positions.Count;
        if (group.Normals.Count != vertexCount
            || group.TexCoords.Count != vertexCount
            || group.Colors.Count != vertexCount
            || group.LightSelectors.Count != vertexCount
            || group.LightBaseColors.Count != vertexCount
            || group.LightNormals.Count != vertexCount
            || group.LightPostScales.Count != vertexCount)
        {
            throw new InvalidDataException("Decoded tfrag primitive has inconsistent vertex attributes.");
        }

        var vertices = new TfragPieceVertex[vertexCount];
        for (var index = 0; index < vertexCount; index++)
        {
            var position = ToPs2Basis(group.Positions[index] - center);
            var normal = ToPs2Basis(group.Normals[index]);
            var texCoord = group.TexCoords[index];
            var color = group.Colors[index];
            var lightBaseColor = group.LightBaseColors[index];
            var lightNormal = ToPs2Basis(group.LightNormals[index]);
            vertices[index] = new TfragPieceVertex(
                ToVector3(position),
                ToVector3(normal),
                new TfragPieceVector2(texCoord.X, texCoord.Y),
                ToVector4(color),
                group.LightSelectors[index],
                ToVector4(lightBaseColor),
                ToVector3(lightNormal),
                group.LightPostScales[index]);
        }

        return new TfragPiecePrimitive(
            new TfragPieceMaterial(group.TextureId, group.ClampU, group.ClampV),
            vertices,
            group.Indices.ToArray());
    }

    private static TfragPieceBounds BuildBounds(IReadOnlyList<TfragPieceVector3> positions, float sourceRadius)
    {
        if (positions.Count == 0)
        {
            return new TfragPieceBounds(new(0, 0, 0), new(0, 0, 0), sourceRadius);
        }

        var minimum = positions[0];
        var maximum = positions[0];
        var radiusSquared = 0f;
        foreach (var position in positions)
        {
            minimum = new(
                MathF.Min(minimum.X, position.X),
                MathF.Min(minimum.Y, position.Y),
                MathF.Min(minimum.Z, position.Z));
            maximum = new(
                MathF.Max(maximum.X, position.X),
                MathF.Max(maximum.Y, position.Y),
                MathF.Max(maximum.Z, position.Z));
            radiusSquared = MathF.Max(
                radiusSquared,
                position.X * position.X + position.Y * position.Y + position.Z * position.Z);
        }

        return new TfragPieceBounds(minimum, maximum, MathF.Sqrt(radiusSquared));
    }

    private static Vector3 ToPs2Basis(Vector3 value) => new(value.X, -value.Z, value.Y);

    private static TfragPieceVector3 ToVector3(Vector3 value) => new(value.X, value.Y, value.Z);

    private static TfragPieceVector4 ToVector4(Vector4 value) => new(value.X, value.Y, value.Z, value.W);
}
