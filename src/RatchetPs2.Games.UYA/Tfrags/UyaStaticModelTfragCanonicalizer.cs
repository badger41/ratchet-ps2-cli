using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static class UyaStaticModelTfragCanonicalizer
{
    // Neutral values observed across retail UYA terrain light rows.
    private const float NeutralLightBase = 6f / 31f;
    private const float NeutralLightPostScale = 4f / 128f;

    public static IReadOnlyList<StaticModelTfragPiece> Build(
        StaticModelCanonicalAsset source,
        IReadOnlyList<StaticModelTfragPartition> partitions,
        IReadOnlyDictionary<int, int> terrainTextureIdsBySourceMaterial,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<int, StaticModelNativeMaterialBinding>? materialBindings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(terrainTextureIdsBySourceMaterial);
        return partitions.Select(partition =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BuildPiece(
                source, partition, terrainTextureIdsBySourceMaterial, materialBindings,
                cancellationToken);
        }).ToArray();
    }

    private static StaticModelTfragPiece BuildPiece(
        StaticModelCanonicalAsset source,
        StaticModelTfragPartition partition,
        IReadOnlyDictionary<int, int> terrainTextureIdsBySourceMaterial,
        IReadOnlyDictionary<int, StaticModelNativeMaterialBinding>? materialBindings,
        CancellationToken cancellationToken)
    {
        var primitives = partition.Primitives.Select(value => BuildPrimitive(
            source, value.Geometry, terrainTextureIdsBySourceMaterial, materialBindings,
            cancellationToken)).ToArray();
        var positions = primitives.SelectMany(value => value.Vertices)
            .Select(value => value.Position).ToArray();
        var geometry = new TfragPieceGeometry(
            Bounds(positions),
            primitives.Select(value => value.Material.TextureId).Where(value => value >= 0)
                .Distinct().Order().ToArray(),
            [new(0, primitives), new(1, []), new(2, [])]);
        var canonicalBytes = TfragPieceCanonicalCodec.Write(geometry);
        var asset = new TfragPieceAsset(
            TfragPieceCanonicalCodec.ComputeContentId(canonicalBytes),
            TfragPieceCanonicalCodec.FormatVersion,
            canonicalBytes,
            geometry);
        return new StaticModelTfragPiece(
            partition.PartitionIndex,
            asset,
            new(QuantizePosition(ToPs2Basis(partition.Origin)), new(0, 0, 0, 1), new(1, 1, 1)),
            partition.SourceMaterialIndices)
        {
            StreamingTextureIds = geometry.TextureIds,
        };
    }

    private static TfragPiecePrimitive BuildPrimitive(
        StaticModelCanonicalAsset source,
        StaticModelPrimitive primitive,
        IReadOnlyDictionary<int, int> terrainTextureIdsBySourceMaterial,
        IReadOnlyDictionary<int, StaticModelNativeMaterialBinding>? materialBindings,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveMaterial(
            source, primitive.MaterialIndex, terrainTextureIdsBySourceMaterial, materialBindings);
        primitive = UyaStaticModelTfragGeometryPreparer.Prepare(
            primitive, resolved.ClampU, resolved.ClampV);
        var material = resolved.Material;
        var normals = primitive.Normals ?? GenerateNormals(primitive, cancellationToken);
        var baseColor = primitive.MaterialIndex is { } materialIndex
            ? source.Materials[materialIndex].BaseColorFactor
            : [1f, 1f, 1f, 1f];
        var vertexCount = primitive.Positions.Length / 3;
        var vertices = new TfragPieceVertex[vertexCount];
        for (var index = 0; index < vertices.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normal = Normalize(new Vector3(
                normals[index * 3], normals[index * 3 + 1], normals[index * 3 + 2]));
            var ps2Normal = ToPs2Basis(normal);
            var color = QuantizeColor(new(
                (primitive.Colors?[index * 4] ?? 1) * baseColor[0],
                (primitive.Colors?[index * 4 + 1] ?? 1) * baseColor[1],
                (primitive.Colors?[index * 4 + 2] ?? 1) * baseColor[2],
                (primitive.Colors?[index * 4 + 3] ?? 1) * baseColor[3]));
            vertices[index] = new(
                QuantizePosition(ToPs2Basis(new Vector3(
                    primitive.Positions[index * 3],
                    primitive.Positions[index * 3 + 1],
                    primitive.Positions[index * 3 + 2]))),
                ps2Normal,
                new(
                    primitive.TexCoords?[index * 2] ?? 0,
                    primitive.TexCoords?[index * 2 + 1] ?? 0),
                color,
                0,
                new(NeutralLightBase, NeutralLightBase, NeutralLightBase, 1),
                QuantizeLightNormal(ps2Normal),
                NeutralLightPostScale);
        }
        return new(material, vertices, primitive.Indices);
    }

    private static (TfragPieceMaterial Material, bool ClampU, bool ClampV) ResolveMaterial(
        StaticModelCanonicalAsset source,
        int? sourceMaterialIndex,
        IReadOnlyDictionary<int, int> terrainTextureIdsBySourceMaterial,
        IReadOnlyDictionary<int, StaticModelNativeMaterialBinding>? materialBindings)
    {
        var materialKey = sourceMaterialIndex ?? -1;
        if (!terrainTextureIdsBySourceMaterial.TryGetValue(materialKey, out var textureId)
            || textureId < 0)
            throw new InvalidDataException($"Source material {materialKey} has no allocated terrain texture ID.");
        StaticModelNativeMaterialBinding resolved;
        if (materialBindings?.TryGetValue(materialKey, out var binding) == true)
        {
            resolved = binding;
        }
        else
        {
            var fallback = RatchetPs2.Games.UYA.Textures.UyaStaticModelMaterialResolver.Resolve(
                source, sourceMaterialIndex, nativeIndex: 0);
            resolved = new(
                fallback.NativeIndex, fallback.SourceIndex, string.Empty,
                fallback.ClampU, fallback.ClampV);
        }
        return (new(textureId, resolved.ClampU, resolved.ClampV), resolved.ClampU, resolved.ClampV);
    }

    private static float[] GenerateNormals(
        StaticModelPrimitive primitive,
        CancellationToken cancellationToken)
    {
        var normals = new Vector3[primitive.Positions.Length / 3];
        for (var offset = 0; offset < primitive.Indices.Length; offset += 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = Position(primitive, primitive.Indices[offset]);
            var b = Position(primitive, primitive.Indices[offset + 1]);
            var c = Position(primitive, primitive.Indices[offset + 2]);
            var normal = Vector3.Cross(b - a, c - a);
            normals[primitive.Indices[offset]] += normal;
            normals[primitive.Indices[offset + 1]] += normal;
            normals[primitive.Indices[offset + 2]] += normal;
        }
        var output = new float[normals.Length * 3];
        for (var index = 0; index < normals.Length; index++)
        {
            var normal = Normalize(normals[index]);
            output[index * 3] = normal.X;
            output[index * 3 + 1] = normal.Y;
            output[index * 3 + 2] = normal.Z;
        }
        return output;
    }

    private static Vector3 Position(StaticModelPrimitive primitive, uint index)
    {
        var offset = checked((int)index * 3);
        return new(primitive.Positions[offset], primitive.Positions[offset + 1], primitive.Positions[offset + 2]);
    }

    private static Vector3 Normalize(Vector3 value) =>
        value.LengthSquared() > 0 && float.IsFinite(value.LengthSquared())
            ? Vector3.Normalize(value)
            : Vector3.UnitY;

    private static TfragPieceBounds Bounds(IReadOnlyList<TfragPieceVector3> positions)
    {
        if (positions.Count == 0) return new(new(0, 0, 0), new(0, 0, 0), 0);
        var minimum = positions[0];
        var maximum = positions[0];
        var radiusSquared = 0f;
        foreach (var position in positions)
        {
            minimum = new(MathF.Min(minimum.X, position.X), MathF.Min(minimum.Y, position.Y), MathF.Min(minimum.Z, position.Z));
            maximum = new(MathF.Max(maximum.X, position.X), MathF.Max(maximum.Y, position.Y), MathF.Max(maximum.Z, position.Z));
            radiusSquared = MathF.Max(radiusSquared,
                position.X * position.X + position.Y * position.Y + position.Z * position.Z);
        }
        return new(minimum, maximum, MathF.Sqrt(radiusSquared));
    }

    private static TfragPieceVector3 ToPs2Basis(TfragPieceVector3 value) =>
        new(value.X, -value.Z, value.Y);

    private static TfragPieceVector3 ToPs2Basis(Vector3 value) =>
        new(value.X, -value.Z, value.Y);

    private static TfragPieceVector3 QuantizePosition(TfragPieceVector3 value) => new(
        MathF.Round(value.X * 1024f, MidpointRounding.ToEven) / 1024f,
        MathF.Round(value.Y * 1024f, MidpointRounding.ToEven) / 1024f,
        MathF.Round(value.Z * 1024f, MidpointRounding.ToEven) / 1024f);

    private static TfragPieceVector4 QuantizeColor(TfragPieceVector4 value) => new(
        QuantizeColorComponent(value.X, 128),
        QuantizeColorComponent(value.Y, 128),
        QuantizeColorComponent(value.Z, 128),
        QuantizeColorComponent(value.W, Ps2Color.FullOpacityAlpha));

    private static float QuantizeColorComponent(float value, int scale)
    {
        var encoded = MathF.Round(value * scale, MidpointRounding.ToEven);
        if (encoded is < 0 or > byte.MaxValue)
            throw new InvalidDataException("Static-model tfrag colors exceed native storage.");
        return encoded / scale;
    }

    private static TfragPieceVector3 QuantizeLightNormal(TfragPieceVector3 value)
    {
        var normal = Vector3.Normalize(new(value.X, value.Y, value.Z));
        var azimuth = AngleByte(MathF.Atan2(normal.Y, normal.X)) * (2f * MathF.PI / 256f);
        var elevation = AngleByte(MathF.Asin(Math.Clamp(normal.Z, -1, 1))) * (2f * MathF.PI / 256f);
        return new(
            MathF.Cos(azimuth) * MathF.Cos(elevation),
            MathF.Sin(azimuth) * MathF.Cos(elevation),
            MathF.Sin(elevation));
    }

    private static int AngleByte(float value)
    {
        var turns = value / (2f * MathF.PI);
        return ((int)MathF.Round(turns * 256f, MidpointRounding.ToEven) % 256 + 256) % 256;
    }

}
