using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static partial class UyaStaticModelTfragWriter
{
    private static readonly TfragPieceVector3 UnitScale = new(1, 1, 1);
    private static readonly TfragPieceVector4 IdentityRotation = new(0, 0, 0, 1);

    private static IReadOnlyList<StaticModelTfragPiece> PrepareForNativeWrite(
        StaticModelTfragPiece piece,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(piece.Asset);
        TfragPieceTransformer.Validate(piece.Asset.Geometry.Bounds, piece.Transform);
        if (piece.Transform.Rotation == IdentityRotation && piece.Transform.Scale == UnitScale)
            return [piece];

        var transformed = TfragPieceTransformer.Transform(
            piece.Asset.Geometry,
            piece.Transform with { Position = new(0, 0, 0) },
            cancellationToken);
        var triangles = BuildTriangles(transformed, cancellationToken);
        var groups = new List<IReadOnlyList<SourceTriangle>>();
        var lod0 = transformed.Lods[0];
        Split(
            triangles,
            groups,
            piece.PartitionIndex,
            Math.Min(UyaStaticModelTfragPartitioner.MaximumVerticesPerPartition,
                lod0.Primitives.Sum(value => value.Vertices.Count)),
            Math.Min(UyaStaticModelTfragPartitioner.MaximumTrianglesPerPartition,
                lod0.Primitives.Sum(value => value.Indices.Count / 3)),
            cancellationToken);
        return groups.Select(group => BuildPiece(piece, transformed, group, cancellationToken)).ToArray();
    }

    private static IReadOnlyList<SourceTriangle> BuildTriangles(
        TfragPieceGeometry geometry,
        CancellationToken cancellationToken)
    {
        if (geometry.Lods.Count != 3 || geometry.Lods[0].Primitives.Count == 0
            || geometry.Lods[1].Primitives.Count != 0 || geometry.Lods[2].Primitives.Count != 0)
            throw new InvalidDataException("Custom tfrag transforms require only LOD0 geometry.");
        var output = new List<SourceTriangle>();
        var nextGeneratedVertexKey = -1L;
        for (var primitiveIndex = 0; primitiveIndex < geometry.Lods[0].Primitives.Count; primitiveIndex++)
        {
            var primitive = geometry.Lods[0].Primitives[primitiveIndex];
            ValidatePrimitive(primitive, primitiveIndex);
            for (var triangleIndex = 0; triangleIndex < primitive.Indices.Count / 3; triangleIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddTriangle(
                    output,
                    primitiveIndex,
                    triangleIndex,
                    OriginalVertex(primitive, primitive.Indices[triangleIndex * 3]),
                    OriginalVertex(primitive, primitive.Indices[triangleIndex * 3 + 1]),
                    OriginalVertex(primitive, primitive.Indices[triangleIndex * 3 + 2]),
                    ref nextGeneratedVertexKey,
                    depth: 0);
            }
        }
        return output;
    }

    private static void Split(
        IReadOnlyList<SourceTriangle> triangles,
        List<IReadOnlyList<SourceTriangle>> output,
        int partitionIndex,
        int maximumVertices,
        int maximumTriangles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = TriangleBounds(triangles);
        if (FitsNativePositionStorage(bounds.Minimum, bounds.Maximum)
            && triangles.Count <= maximumTriangles
            && triangles.GroupBy(value => value.PrimitiveIndex).Sum(group => group
                .SelectMany(value => value.Vertices).Select(value => value.Key).Distinct().Count()) <= maximumVertices)
        {
            output.Add(triangles);
            return;
        }
        if (triangles.Count == 1)
            throw new InvalidDataException(
                $"Custom tfrag partition {partitionIndex} contains a triangle that is too large for native position storage after scaling; reduce the scale.");

        var axis = LargestAxis(bounds.Minimum, bounds.Maximum);
        var ordered = triangles.OrderBy(value => Coordinate(value.Center, axis))
            .ThenBy(value => value.PrimitiveIndex)
            .ThenBy(value => value.TriangleIndex)
            .ThenBy(value => value.Order)
            .ToArray();
        var middle = ordered.Length / 2;
        Split(ordered[..middle], output, partitionIndex, maximumVertices, maximumTriangles, cancellationToken);
        Split(ordered[middle..], output, partitionIndex, maximumVertices, maximumTriangles, cancellationToken);
    }

    private static StaticModelTfragPiece BuildPiece(
        StaticModelTfragPiece source,
        TfragPieceGeometry transformed,
        IReadOnlyList<SourceTriangle> triangles,
        CancellationToken cancellationToken)
    {
        var sourcePrimitives = transformed.Lods[0].Primitives;
        var bounds = TriangleBounds(triangles);
        var center = Quantize(new(
            (bounds.Minimum.X + bounds.Maximum.X) / 2,
            (bounds.Minimum.Y + bounds.Maximum.Y) / 2,
            (bounds.Minimum.Z + bounds.Maximum.Z) / 2));
        var primitives = new List<TfragPiecePrimitive>();
        foreach (var byPrimitive in triangles.GroupBy(value => value.PrimitiveIndex).OrderBy(value => value.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = sourcePrimitives[byPrimitive.Key];
            var vertices = new List<TfragPieceVertex>();
            var indices = new List<uint>();
            var remap = new Dictionary<long, uint>();
            foreach (var triangle in byPrimitive.OrderBy(value => value.TriangleIndex).ThenBy(value => value.Order))
            {
                var rebased = triangle.Vertices.Select(sourceVertex => sourceVertex with
                {
                    Value = sourceVertex.Value with
                    {
                        Position = Quantize(new(
                            sourceVertex.Value.Position.X - center.X,
                            sourceVertex.Value.Position.Y - center.Y,
                            sourceVertex.Value.Position.Z - center.Z)),
                    },
                }).ToArray();
                if (Collapsed(
                        rebased[0].Value.Position,
                        rebased[1].Value.Position,
                        rebased[2].Value.Position))
                    continue;
                foreach (var sourceVertex in rebased)
                {
                    if (!remap.TryGetValue(sourceVertex.Key, out var targetIndex))
                    {
                        targetIndex = checked((uint)vertices.Count);
                        remap.Add(sourceVertex.Key, targetIndex);
                        vertices.Add(sourceVertex.Value);
                    }
                    indices.Add(targetIndex);
                }
            }
            if (indices.Count > 0) primitives.Add(new(original.Material, vertices, indices));
        }

        var positions = primitives.SelectMany(value => value.Vertices).Select(value => value.Position).ToArray();
        var geometry = new TfragPieceGeometry(
            GeometryBounds(positions),
            primitives.Select(value => value.Material.TextureId).Where(value => value >= 0)
                .Distinct().Order().ToArray(),
            [new(0, primitives), new(1, []), new(2, [])]);
        var canonical = TfragPieceCanonicalCodec.Write(geometry);
        return source with
        {
            Asset = new(
                TfragPieceCanonicalCodec.ComputeContentId(canonical),
                TfragPieceCanonicalCodec.FormatVersion,
                canonical,
                geometry),
            Transform = new(
                new(
                    source.Transform.Position.X + center.X,
                    source.Transform.Position.Y + center.Y,
                    source.Transform.Position.Z + center.Z),
                IdentityRotation,
                UnitScale),
            StreamingTextureIds = geometry.TextureIds,
        };
    }

    private static (TfragPieceVector3 Minimum, TfragPieceVector3 Maximum) TriangleBounds(
        IReadOnlyList<SourceTriangle> triangles)
    {
        if (triangles.Count == 0) throw new InvalidDataException("Custom tfrag transform produced an empty group.");
        var minimum = triangles[0].Minimum;
        var maximum = triangles[0].Maximum;
        foreach (var triangle in triangles.Skip(1))
        {
            minimum = new(
                MathF.Min(minimum.X, triangle.Minimum.X),
                MathF.Min(minimum.Y, triangle.Minimum.Y),
                MathF.Min(minimum.Z, triangle.Minimum.Z));
            maximum = new(
                MathF.Max(maximum.X, triangle.Maximum.X),
                MathF.Max(maximum.Y, triangle.Maximum.Y),
                MathF.Max(maximum.Z, triangle.Maximum.Z));
        }
        return (minimum, maximum);
    }

    private static void AddTriangle(
        List<SourceTriangle> output,
        int primitiveIndex,
        int triangleIndex,
        SourceVertex a,
        SourceVertex b,
        SourceVertex c,
        ref long nextGeneratedVertexKey,
        int depth)
    {
        var minimum = new TfragPieceVector3(
            MathF.Min(a.Value.Position.X, MathF.Min(b.Value.Position.X, c.Value.Position.X)),
            MathF.Min(a.Value.Position.Y, MathF.Min(b.Value.Position.Y, c.Value.Position.Y)),
            MathF.Min(a.Value.Position.Z, MathF.Min(b.Value.Position.Z, c.Value.Position.Z)));
        var maximum = new TfragPieceVector3(
            MathF.Max(a.Value.Position.X, MathF.Max(b.Value.Position.X, c.Value.Position.X)),
            MathF.Max(a.Value.Position.Y, MathF.Max(b.Value.Position.Y, c.Value.Position.Y)),
            MathF.Max(a.Value.Position.Z, MathF.Max(b.Value.Position.Z, c.Value.Position.Z)));
        if (FitsNativePositionStorage(minimum, maximum))
        {
            output.Add(new(
                primitiveIndex,
                triangleIndex,
                output.Count,
                [a, b, c],
                minimum,
                maximum,
                new(
                    (a.Value.Position.X + b.Value.Position.X + c.Value.Position.X) / 3,
                    (a.Value.Position.Y + b.Value.Position.Y + c.Value.Position.Y) / 3,
                    (a.Value.Position.Z + b.Value.Position.Z + c.Value.Position.Z) / 3)));
            return;
        }
        if (depth >= 32)
            throw new InvalidDataException("Custom tfrag triangle subdivision did not converge on native position storage.");

        var ab = DistanceSquared(a.Value.Position, b.Value.Position);
        var bc = DistanceSquared(b.Value.Position, c.Value.Position);
        var ca = DistanceSquared(c.Value.Position, a.Value.Position);
        if (ab >= bc && ab >= ca)
        {
            var middle = Midpoint(a, b, ref nextGeneratedVertexKey);
            AddTriangle(output, primitiveIndex, triangleIndex, a, middle, c, ref nextGeneratedVertexKey, depth + 1);
            AddTriangle(output, primitiveIndex, triangleIndex, middle, b, c, ref nextGeneratedVertexKey, depth + 1);
        }
        else if (bc >= ca)
        {
            var middle = Midpoint(b, c, ref nextGeneratedVertexKey);
            AddTriangle(output, primitiveIndex, triangleIndex, a, b, middle, ref nextGeneratedVertexKey, depth + 1);
            AddTriangle(output, primitiveIndex, triangleIndex, a, middle, c, ref nextGeneratedVertexKey, depth + 1);
        }
        else
        {
            var middle = Midpoint(c, a, ref nextGeneratedVertexKey);
            AddTriangle(output, primitiveIndex, triangleIndex, a, b, middle, ref nextGeneratedVertexKey, depth + 1);
            AddTriangle(output, primitiveIndex, triangleIndex, middle, b, c, ref nextGeneratedVertexKey, depth + 1);
        }
    }

    private static SourceVertex OriginalVertex(TfragPiecePrimitive primitive, uint index) =>
        new(checked((long)index + 1), primitive.Vertices[checked((int)index)]);

    private static SourceVertex Midpoint(SourceVertex left, SourceVertex right, ref long nextGeneratedVertexKey) => new(
        nextGeneratedVertexKey--,
        new(
        Quantize(Average(left.Value.Position, right.Value.Position)),
        Normalize(Average(left.Value.Normal, right.Value.Normal)),
        new(
            (left.Value.TexCoord.X + right.Value.TexCoord.X) / 2,
            (left.Value.TexCoord.Y + right.Value.TexCoord.Y) / 2),
        new(
            (left.Value.Color.X + right.Value.Color.X) / 2,
            (left.Value.Color.Y + right.Value.Color.Y) / 2,
            (left.Value.Color.Z + right.Value.Color.Z) / 2,
            (left.Value.Color.W + right.Value.Color.W) / 2),
        (left.Value.LightSelector + right.Value.LightSelector) / 2,
        new(
            (left.Value.LightBaseColor.X + right.Value.LightBaseColor.X) / 2,
            (left.Value.LightBaseColor.Y + right.Value.LightBaseColor.Y) / 2,
            (left.Value.LightBaseColor.Z + right.Value.LightBaseColor.Z) / 2,
            (left.Value.LightBaseColor.W + right.Value.LightBaseColor.W) / 2),
        Normalize(Average(left.Value.LightNormal, right.Value.LightNormal)),
        (left.Value.LightPostScale + right.Value.LightPostScale) / 2));

    private static TfragPieceVector3 Average(TfragPieceVector3 left, TfragPieceVector3 right) => new(
        (left.X + right.X) / 2,
        (left.Y + right.Y) / 2,
        (left.Z + right.Z) / 2);

    private static TfragPieceVector3 Normalize(TfragPieceVector3 value)
    {
        var length = MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length > 0 && float.IsFinite(length)
            ? new(value.X / length, value.Y / length, value.Z / length)
            : new(0, 0, 0);
    }

    private static float DistanceSquared(TfragPieceVector3 left, TfragPieceVector3 right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        var z = left.Z - right.Z;
        return x * x + y * y + z * z;
    }

    private static bool Collapsed(
        TfragPieceVector3 a,
        TfragPieceVector3 b,
        TfragPieceVector3 c)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var abZ = b.Z - a.Z;
        var acX = c.X - a.X;
        var acY = c.Y - a.Y;
        var acZ = c.Z - a.Z;
        var crossX = abY * acZ - abZ * acY;
        var crossY = abZ * acX - abX * acZ;
        var crossZ = abX * acY - abY * acX;
        return crossX * crossX + crossY * crossY + crossZ * crossZ <= 0.00000001f;
    }

    private static TfragPieceBounds GeometryBounds(IReadOnlyList<TfragPieceVector3> positions)
    {
        if (positions.Count == 0) throw new InvalidDataException("Custom tfrag transform produced no vertices.");
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
            radiusSquared = MathF.Max(radiusSquared,
                position.X * position.X + position.Y * position.Y + position.Z * position.Z);
        }
        return new(minimum, maximum, MathF.Sqrt(radiusSquared));
    }

    private static bool FitsNativePositionStorage(TfragPieceVector3 minimum, TfragPieceVector3 maximum)
    {
        var maximumSpan = UyaStaticModelTfragPartitioner.MaximumLocalCoordinate * 2;
        return maximum.X - minimum.X <= maximumSpan
            && maximum.Y - minimum.Y <= maximumSpan
            && maximum.Z - minimum.Z <= maximumSpan;
    }

    private static int LargestAxis(TfragPieceVector3 minimum, TfragPieceVector3 maximum)
    {
        var x = maximum.X - minimum.X;
        var y = maximum.Y - minimum.Y;
        var z = maximum.Z - minimum.Z;
        return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
    }

    private static float Coordinate(TfragPieceVector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    private static TfragPieceVector3 Quantize(TfragPieceVector3 value) => new(
        MathF.Round(value.X * 1024, MidpointRounding.ToEven) / 1024,
        MathF.Round(value.Y * 1024, MidpointRounding.ToEven) / 1024,
        MathF.Round(value.Z * 1024, MidpointRounding.ToEven) / 1024);

    private sealed record SourceTriangle(
        int PrimitiveIndex,
        int TriangleIndex,
        int Order,
        IReadOnlyList<SourceVertex> Vertices,
        TfragPieceVector3 Minimum,
        TfragPieceVector3 Maximum,
        TfragPieceVector3 Center);

    private sealed record SourceVertex(long Key, TfragPieceVertex Value);
}
