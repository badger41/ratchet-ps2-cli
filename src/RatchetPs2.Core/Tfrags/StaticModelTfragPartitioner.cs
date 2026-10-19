using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Tfrags;

public sealed record StaticModelTfragPartitionLimits(
    int MaximumVertices,
    int MaximumTriangles,
    int MaximumMaterials,
    float MaximumLocalCoordinate = float.PositiveInfinity);

public sealed record StaticModelTfragPrimitive(
    StaticModelPrimitive Geometry,
    IReadOnlyList<int> SourceTriangleIndices);

public sealed record StaticModelTfragPartition(
    int PartitionIndex,
    TfragPieceVector3 Origin,
    StaticModelBounds SourceBounds,
    StaticModelBounds LocalBounds,
    float Radius,
    IReadOnlyList<int> SourceMaterialIndices,
    IReadOnlyList<StaticModelTfragPrimitive> Primitives,
    int VertexCount,
    int TriangleCount);

public sealed record StaticModelTfragPiece(
    int PartitionIndex,
    TfragPieceAsset Asset,
    TfragPieceTransform Transform,
    IReadOnlyList<int> SourceMaterialIndices)
{
    public IReadOnlyList<int> StreamingTextureIds { get; init; } = [];
}

public static class StaticModelTfragPartitioner
{
    public static IReadOnlyList<StaticModelTfragPartition> Partition(
        StaticModelCanonicalAsset asset,
        StaticModelTfragPartitionLimits limits,
        CancellationToken cancellationToken = default) =>
        Partition(asset, limits, requiredPackedPartitions: null, cancellationToken);

    internal static IReadOnlyList<StaticModelTfragPartition> Partition(
        StaticModelCanonicalAsset asset,
        StaticModelTfragPartitionLimits limits,
        Func<StaticModelTfragPartition, int>? requiredPackedPartitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits);
        var triangles = BuildTriangles(asset, cancellationToken);
        if (triangles.Count == 0) return [];

        var groups = new List<Triangle[]>();
        Split(asset, triangles.ToArray(), limits, requiredPackedPartitions, groups, cancellationToken);
        return groups.Select((group, index) => BuildPartition(asset, group, index, cancellationToken))
            .ToArray();
    }

    private static List<Triangle> BuildTriangles(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset.Primitives);
        var triangles = new List<Triangle>();
        for (var primitiveIndex = 0; primitiveIndex < asset.Primitives.Count; primitiveIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primitive = asset.Primitives[primitiveIndex]
                ?? throw new InvalidDataException($"Static model primitive {primitiveIndex} is missing.");
            ArgumentNullException.ThrowIfNull(primitive.Positions);
            ArgumentNullException.ThrowIfNull(primitive.Indices);
            var vertexCount = primitive.Positions.Length / 3;
            if (primitive.Positions.Length == 0 || primitive.Positions.Length % 3 != 0
                || primitive.Indices.Length == 0 || primitive.Indices.Length % 3 != 0
                || primitive.Indices.Any(index => index >= (uint)vertexCount))
                throw new InvalidDataException($"Static model primitive {primitiveIndex} has invalid triangles.");
            if (primitive.MaterialIndex is { } materialIndex
                && (materialIndex < 0 || materialIndex >= asset.Materials.Count))
                throw new InvalidDataException($"Static model primitive {primitiveIndex} has an invalid material.");

            for (var triangleIndex = 0; triangleIndex < primitive.Indices.Length / 3; triangleIndex++)
            {
                var first = primitive.Indices[triangleIndex * 3];
                var second = primitive.Indices[triangleIndex * 3 + 1];
                var third = primitive.Indices[triangleIndex * 3 + 2];
                var x = Coordinates(primitive.Positions, first, second, third, 0);
                var y = Coordinates(primitive.Positions, first, second, third, 1);
                var z = Coordinates(primitive.Positions, first, second, third, 2);
                triangles.Add(new(
                    primitiveIndex,
                    triangleIndex,
                    primitive.MaterialIndex ?? -1,
                    first,
                    second,
                    third,
                    x.Average,
                    y.Average,
                    z.Average,
                    x.Minimum,
                    y.Minimum,
                    z.Minimum,
                    x.Maximum,
                    y.Maximum,
                    z.Maximum));
            }
        }
        return triangles;
    }

    private static void Split(
        StaticModelCanonicalAsset asset,
        Triangle[] triangles,
        StaticModelTfragPartitionLimits limits,
        Func<StaticModelTfragPartition, int>? requiredPackedPartitions,
        List<Triangle[]> output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vertexCount = CountVertices(triangles);
        var materialCount = triangles.Select(value => value.MaterialIndex).Distinct().Count();
        var requiredPartitions = Math.Max(
            DivideRoundUp(triangles.Length, limits.MaximumTriangles),
            Math.Max(
                DivideRoundUp(vertexCount, limits.MaximumVertices),
                DivideRoundUp(materialCount, limits.MaximumMaterials)));
        if (!FitsLocalCoordinates(triangles, limits.MaximumLocalCoordinate))
            requiredPartitions = Math.Max(requiredPartitions, 2);
        if (requiredPartitions <= 1 && requiredPackedPartitions is not null)
            requiredPartitions = requiredPackedPartitions(
                BuildPartition(asset, triangles, 0, cancellationToken));
        if (requiredPartitions <= 1)
        {
            output.Add(triangles.OrderBy(value => value.PrimitiveIndex)
                .ThenBy(value => value.TriangleIndex).ToArray());
            return;
        }
        if (triangles.Length == 1)
            throw new InvalidDataException(
                "A static-model triangle exceeds the target tfrag local coordinate range.");

        var axis = LongestCentroidAxis(triangles);
        var ordered = triangles.OrderBy(value => value.Centroid(axis))
            .ThenBy(value => value.PrimitiveIndex)
            .ThenBy(value => value.TriangleIndex)
            .ToArray();
        var leftPartitions = requiredPartitions / 2;
        var splitIndex = Math.Clamp(
            checked((int)Math.Round(
                ordered.Length * (double)leftPartitions / requiredPartitions,
                MidpointRounding.ToEven)),
            1,
            ordered.Length - 1);
        Split(asset, ordered[..splitIndex], limits, requiredPackedPartitions, output, cancellationToken);
        Split(asset, ordered[splitIndex..], limits, requiredPackedPartitions, output, cancellationToken);
    }

    private static StaticModelTfragPartition BuildPartition(
        StaticModelCanonicalAsset asset,
        Triangle[] triangles,
        int partitionIndex,
        CancellationToken cancellationToken)
    {
        var sourceBounds = Bounds(asset, triangles);
        var origin = new TfragPieceVector3(
            Midpoint(sourceBounds.MinimumX, sourceBounds.MaximumX),
            Midpoint(sourceBounds.MinimumY, sourceBounds.MaximumY),
            Midpoint(sourceBounds.MinimumZ, sourceBounds.MaximumZ));
        var primitives = triangles.GroupBy(value => value.PrimitiveIndex)
            .OrderBy(value => value.Key)
            .Select(group => BuildPrimitive(asset.Primitives[group.Key], group.ToArray(), origin))
            .ToArray();
        var localBounds = new StaticModelBounds(
            sourceBounds.MinimumX - origin.X,
            sourceBounds.MinimumY - origin.Y,
            sourceBounds.MinimumZ - origin.Z,
            sourceBounds.MaximumX - origin.X,
            sourceBounds.MaximumY - origin.Y,
            sourceBounds.MaximumZ - origin.Z);
        var radiusSquared = 0f;
        foreach (var primitive in primitives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var positions = primitive.Geometry.Positions;
            for (var offset = 0; offset < positions.Length; offset += 3)
            {
                radiusSquared = MathF.Max(radiusSquared,
                    positions[offset] * positions[offset]
                    + positions[offset + 1] * positions[offset + 1]
                    + positions[offset + 2] * positions[offset + 2]);
            }
        }
        var radius = MathF.Sqrt(radiusSquared);
        if (!float.IsFinite(radius))
            throw new InvalidDataException("Static model tfrag partition radius is not finite.");
        return new(
            partitionIndex,
            origin,
            sourceBounds,
            localBounds,
            radius,
            triangles.Select(value => value.MaterialIndex).Distinct().Order().ToArray(),
            primitives,
            primitives.Sum(value => value.Geometry.Positions.Length / 3),
            triangles.Length);
    }

    private static StaticModelTfragPrimitive BuildPrimitive(
        StaticModelPrimitive source,
        IReadOnlyList<Triangle> triangles,
        TfragPieceVector3 origin)
    {
        var sourceIndices = new List<uint>();
        var remap = new Dictionary<uint, uint>();
        var outputIndices = new uint[triangles.Count * 3];
        for (var triangleOffset = 0; triangleOffset < triangles.Count; triangleOffset++)
        {
            var triangle = triangles[triangleOffset];
            for (var corner = 0; corner < 3; corner++)
            {
                var sourceIndex = triangle.Index(corner);
                if (!remap.TryGetValue(sourceIndex, out var outputIndex))
                {
                    outputIndex = checked((uint)sourceIndices.Count);
                    remap.Add(sourceIndex, outputIndex);
                    sourceIndices.Add(sourceIndex);
                }
                outputIndices[triangleOffset * 3 + corner] = outputIndex;
            }
        }

        var positions = Copy(source.Positions, sourceIndices, 3);
        for (var offset = 0; offset < positions.Length; offset += 3)
        {
            positions[offset] -= origin.X;
            positions[offset + 1] -= origin.Y;
            positions[offset + 2] -= origin.Z;
        }
        var geometry = source with
        {
            Positions = positions,
            Normals = source.Normals is null ? null : Copy(source.Normals, sourceIndices, 3),
            TexCoords = source.TexCoords is null ? null : Copy(source.TexCoords, sourceIndices, 2),
            Colors = source.Colors is null ? null : Copy(source.Colors, sourceIndices, 4),
            Indices = outputIndices,
        };
        return new(geometry, triangles.Select(value => value.TriangleIndex).ToArray());
    }

    private static float[] Copy(float[] source, IReadOnlyList<uint> indices, int components)
    {
        var output = new float[checked(indices.Count * components)];
        for (var outputIndex = 0; outputIndex < indices.Count; outputIndex++)
        {
            var sourceOffset = checked((int)indices[outputIndex] * components);
            if (sourceOffset < 0 || sourceOffset > source.Length - components)
                throw new InvalidDataException("Static model vertex attributes do not match its positions.");
            source.AsSpan(sourceOffset, components).CopyTo(output.AsSpan(outputIndex * components, components));
        }
        if (output.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Static model vertex attributes contain a non-finite value.");
        return output;
    }

    private static StaticModelBounds Bounds(StaticModelCanonicalAsset asset, IReadOnlyList<Triangle> triangles)
    {
        var minimumX = float.PositiveInfinity;
        var minimumY = float.PositiveInfinity;
        var minimumZ = float.PositiveInfinity;
        var maximumX = float.NegativeInfinity;
        var maximumY = float.NegativeInfinity;
        var maximumZ = float.NegativeInfinity;
        foreach (var triangle in triangles)
        {
            var positions = asset.Primitives[triangle.PrimitiveIndex].Positions;
            for (var corner = 0; corner < 3; corner++)
            {
                var index = triangle.Index(corner);
                var x = Coordinate(positions, index, 0);
                var y = Coordinate(positions, index, 1);
                var z = Coordinate(positions, index, 2);
                minimumX = MathF.Min(minimumX, x);
                minimumY = MathF.Min(minimumY, y);
                minimumZ = MathF.Min(minimumZ, z);
                maximumX = MathF.Max(maximumX, x);
                maximumY = MathF.Max(maximumY, y);
                maximumZ = MathF.Max(maximumZ, z);
            }
        }
        return new(minimumX, minimumY, minimumZ, maximumX, maximumY, maximumZ);
    }

    private static int CountVertices(IEnumerable<Triangle> triangles)
    {
        var vertices = new HashSet<VertexKey>();
        foreach (var triangle in triangles)
        {
            vertices.Add(new(triangle.PrimitiveIndex, triangle.First));
            vertices.Add(new(triangle.PrimitiveIndex, triangle.Second));
            vertices.Add(new(triangle.PrimitiveIndex, triangle.Third));
        }
        return vertices.Count;
    }

    private static int LongestCentroidAxis(IReadOnlyList<Triangle> triangles)
    {
        var ranges = Enumerable.Range(0, 3).Select(axis =>
        {
            var values = triangles.Select(value => value.Centroid(axis));
            return values.Max() - values.Min();
        }).ToArray();
        return ranges[1] > ranges[0] && ranges[1] >= ranges[2]
            ? 1
            : ranges[2] > ranges[0] && ranges[2] > ranges[1] ? 2 : 0;
    }

    private static bool FitsLocalCoordinates(
        IReadOnlyList<Triangle> triangles,
        float maximumLocalCoordinate)
    {
        if (float.IsPositiveInfinity(maximumLocalCoordinate)) return true;
        return Enumerable.Range(0, 3).All(axis =>
            triangles.Max(value => value.Maximum(axis))
                - triangles.Min(value => value.Minimum(axis))
            <= maximumLocalCoordinate * 2f);
    }

    private static (float Minimum, float Maximum, float Average) Coordinates(
        float[] positions,
        uint first,
        uint second,
        uint third,
        int component)
    {
        var a = Coordinate(positions, first, component);
        var b = Coordinate(positions, second, component);
        var c = Coordinate(positions, third, component);
        return (MathF.Min(a, MathF.Min(b, c)), MathF.Max(a, MathF.Max(b, c)), (a + b + c) / 3f);
    }

    private static float Coordinate(float[] positions, uint index, int component)
    {
        var value = positions[checked((int)index * 3 + component)];
        if (!float.IsFinite(value)) throw new InvalidDataException("Static model position is not finite.");
        return value;
    }

    private static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);

    private static float Midpoint(float minimum, float maximum)
    {
        var midpoint = (float)(((double)minimum + maximum) / 2d);
        if (!float.IsFinite(midpoint))
            throw new InvalidDataException("Static model tfrag partition origin is not finite.");
        return midpoint;
    }

    private static void ValidateLimits(StaticModelTfragPartitionLimits limits)
    {
        if (limits.MaximumVertices < 3 || limits.MaximumTriangles <= 0 || limits.MaximumMaterials <= 0
            || float.IsNaN(limits.MaximumLocalCoordinate) || limits.MaximumLocalCoordinate <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "Tfrag partition limits must be positive.");
    }

    private readonly record struct VertexKey(int PrimitiveIndex, uint VertexIndex);

    private readonly record struct Triangle(
        int PrimitiveIndex,
        int TriangleIndex,
        int MaterialIndex,
        uint First,
        uint Second,
        uint Third,
        float CentroidX,
        float CentroidY,
        float CentroidZ,
        float MinimumX,
        float MinimumY,
        float MinimumZ,
        float MaximumX,
        float MaximumY,
        float MaximumZ)
    {
        public uint Index(int corner) => corner switch
        {
            0 => First,
            1 => Second,
            2 => Third,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };

        public float Centroid(int axis) => axis switch
        {
            0 => CentroidX,
            1 => CentroidY,
            2 => CentroidZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        public float Minimum(int axis) => axis switch
        {
            0 => MinimumX,
            1 => MinimumY,
            2 => MinimumZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        public float Maximum(int axis) => axis switch
        {
            0 => MaximumX,
            1 => MaximumY,
            2 => MaximumZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }
}
