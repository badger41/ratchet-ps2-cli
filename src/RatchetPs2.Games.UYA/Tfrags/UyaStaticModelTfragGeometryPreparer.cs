using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Textures;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static class UyaStaticModelTfragGeometryPreparer
{
    public static StaticModelCanonicalAsset Prepare(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken)
    {
        var primitives = asset.Primitives.Select(primitive =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var material = UyaStaticModelMaterialResolver.Resolve(
                asset, primitive.MaterialIndex, nativeIndex: 0);
            return Prepare(primitive, material.ClampU, material.ClampV);
        }).ToArray();
        return asset with { Primitives = primitives };
    }

    public static StaticModelPrimitive Prepare(
        StaticModelPrimitive primitive,
        bool clampU,
        bool clampV)
    {
        if (primitive.TexCoords is null) return primitive;
        var vertexCount = primitive.Positions.Length / 3;
        if (primitive.TexCoords.Length != vertexCount * 2)
            throw new InvalidDataException("Static-model tfrag texture-coordinate count does not match its vertices.");
        if (primitive.TexCoords.All(FitsNativeTexCoord))
            return primitive with { TexCoords = primitive.TexCoords.Select(QuantizeTexCoord).ToArray() };

        var expandedVertexCount = primitive.Indices.Length;
        var positions = new float[expandedVertexCount * 3];
        var normals = primitive.Normals is null ? null : new float[expandedVertexCount * 3];
        var texCoords = new float[expandedVertexCount * 2];
        var colors = primitive.Colors is null ? null : new float[expandedVertexCount * 4];
        var indices = new uint[expandedVertexCount];
        for (var offset = 0; offset < primitive.Indices.Length; offset += 3)
        {
            var firstIndex = checked((int)primitive.Indices[offset]);
            var secondIndex = checked((int)primitive.Indices[offset + 1]);
            var thirdIndex = checked((int)primitive.Indices[offset + 2]);
            var adjusted = NormalizeTriangleTexCoords(
                TexCoord(primitive.TexCoords, firstIndex),
                TexCoord(primitive.TexCoords, secondIndex),
                TexCoord(primitive.TexCoords, thirdIndex),
                clampU,
                clampV);
            for (var corner = 0; corner < 3; corner++)
            {
                var target = offset + corner;
                var sourceIndex = corner switch
                {
                    0 => firstIndex,
                    1 => secondIndex,
                    _ => thirdIndex,
                };
                Copy(primitive.Positions, sourceIndex, positions, target, 3);
                if (normals is not null) Copy(primitive.Normals!, sourceIndex, normals, target, 3);
                if (colors is not null) Copy(primitive.Colors!, sourceIndex, colors, target, 4);
                texCoords[target * 2] = adjusted[corner].X;
                texCoords[target * 2 + 1] = adjusted[corner].Y;
                indices[target] = checked((uint)target);
            }
        }
        return primitive with
        {
            Positions = positions,
            Normals = normals,
            TexCoords = texCoords,
            Colors = colors,
            Indices = indices,
        };
    }

    private static (float X, float Y)[] NormalizeTriangleTexCoords(
        (float X, float Y) first,
        (float X, float Y) second,
        (float X, float Y) third,
        bool clampU,
        bool clampV)
    {
        var repeatU = !clampU;
        var repeatV = !clampV;
        var originU = repeatU ? MathF.Floor(first.X) : 0;
        var originV = repeatV ? MathF.Floor(first.Y) : 0;
        var adjusted = GltfTexCoordUtils.AdjustTriangleTexCoords(
            new(first.X - originU, first.Y - originV),
            new(second.X - originU, second.Y - originV),
            new(third.X - originU, third.Y - originV),
            textureSize: null,
            repeatU,
            repeatV,
            normalizeClampedAxes: true);
        var u = FitNativeTexCoordAxis(adjusted[0].X, adjusted[1].X, adjusted[2].X, repeatU);
        var v = FitNativeTexCoordAxis(adjusted[0].Y, adjusted[1].Y, adjusted[2].Y, repeatV);
        return
        [
            (QuantizeTexCoord(u[0]), QuantizeTexCoord(v[0])),
            (QuantizeTexCoord(u[1]), QuantizeTexCoord(v[1])),
            (QuantizeTexCoord(u[2]), QuantizeTexCoord(v[2])),
        ];
    }

    private static float[] FitNativeTexCoordAxis(float first, float second, float third, bool repeat)
    {
        if (!float.IsFinite(first) || !float.IsFinite(second) || !float.IsFinite(third))
            throw new InvalidDataException("Static-model tfrag texture coordinates must be finite.");
        if (!repeat) return [first, second, third];

        const float minimum = -4f;
        const float maximum = short.MaxValue / 4096f;
        var low = MathF.Min(first, MathF.Min(second, third));
        var high = MathF.Max(first, MathF.Max(second, third));
        var minimumOffset = MathF.Ceiling(high - maximum);
        var maximumOffset = MathF.Floor(low - minimum);
        if (minimumOffset > maximumOffset)
            throw new InvalidDataException(
                "Static-model tfrag triangle texture coordinates span too many repeated tiles for native storage.");
        var preferredOffset = MathF.Round((low + high - minimum - maximum) * 0.5f);
        var offset = Math.Clamp(preferredOffset, minimumOffset, maximumOffset);
        return [first - offset, second - offset, third - offset];
    }

    private static (float X, float Y) TexCoord(float[] values, int index) =>
        (values[index * 2], values[index * 2 + 1]);

    private static void Copy(float[] source, int sourceIndex, float[] target, int targetIndex, int width) =>
        source.AsSpan(sourceIndex * width, width).CopyTo(target.AsSpan(targetIndex * width, width));

    private static float QuantizeTexCoord(float value)
    {
        var encoded = MathF.Round(value * (value < 0 ? 8192f : 4096f), MidpointRounding.ToEven);
        if (encoded is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException("Static-model tfrag texture coordinates exceed native storage.");
        var decoded = (short)encoded / 4096f;
        return decoded < 0 ? decoded * 0.5f : decoded;
    }

    private static bool FitsNativeTexCoord(float value)
    {
        var encoded = MathF.Round(value * (value < 0 ? 8192f : 4096f), MidpointRounding.ToEven);
        return float.IsFinite(encoded) && encoded is >= short.MinValue and <= short.MaxValue;
    }
}
