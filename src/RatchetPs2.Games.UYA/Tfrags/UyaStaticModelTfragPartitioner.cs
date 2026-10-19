using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static class UyaStaticModelTfragPartitioner
{
    // Generated base-only groups need more topology headroom than the 92-vertex retail maximum.
    public const int MaximumVerticesPerPartition = 80;
    public const int MaximumTrianglesPerPartition = 128;
    public const int MaximumMaterialsPerPartition = 12;
    public const int MaximumTextureDimension = 128;
    public const float MaximumLocalCoordinate = 31f;

    public static IReadOnlyList<StaticModelTfragPartition> Partition(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken = default)
    {
        asset = NormalizeOversizedSourceUnits(asset, cancellationToken);
        asset = UyaStaticModelTfragGeometryPreparer.Prepare(asset, cancellationToken);
        var partitions = StaticModelTfragPartitioner.Partition(
            asset,
            new(
                MaximumVerticesPerPartition,
                MaximumTrianglesPerPartition,
                MaximumMaterialsPerPartition,
                MaximumLocalCoordinate),
            partition => RequiredPackedPartitions(partition, cancellationToken),
            cancellationToken);
        return RemoveCollapsedTriangles(partitions, cancellationToken);
    }

    private static IReadOnlyList<StaticModelTfragPartition> RemoveCollapsedTriangles(
        IReadOnlyList<StaticModelTfragPartition> partitions,
        CancellationToken cancellationToken)
    {
        var output = new List<StaticModelTfragPartition>(partitions.Count);
        foreach (var partition in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primitives = partition.Primitives.Select(primitive =>
            {
                var indices = new List<uint>(primitive.Geometry.Indices.Length);
                var sourceTriangles = new List<int>(primitive.SourceTriangleIndices.Count);
                for (var triangle = 0; triangle < primitive.Geometry.Indices.Length / 3; triangle++)
                {
                    if (CollapsedAfterPositionQuantization(primitive.Geometry, triangle)) continue;
                    indices.Add(primitive.Geometry.Indices[triangle * 3]);
                    indices.Add(primitive.Geometry.Indices[triangle * 3 + 1]);
                    indices.Add(primitive.Geometry.Indices[triangle * 3 + 2]);
                    sourceTriangles.Add(primitive.SourceTriangleIndices[triangle]);
                }
                return new StaticModelTfragPrimitive(
                    primitive.Geometry with { Indices = indices.ToArray() },
                    sourceTriangles);
            }).Where(value => value.Geometry.Indices.Length != 0).ToArray();
            if (primitives.Length == 0) continue;
            output.Add(partition with
            {
                PartitionIndex = output.Count,
                SourceMaterialIndices = primitives.Select(value => value.Geometry.MaterialIndex ?? -1)
                    .Distinct().Order().ToArray(),
                Primitives = primitives,
                VertexCount = primitives.Sum(value => value.Geometry.Positions.Length / 3),
                TriangleCount = primitives.Sum(value => value.Geometry.Indices.Length / 3),
            });
        }
        return output;
    }

    private static bool CollapsedAfterPositionQuantization(StaticModelPrimitive primitive, int triangle)
    {
        Span<float> positions = stackalloc float[9];
        for (var corner = 0; corner < 3; corner++)
        {
            var sourceOffset = checked((int)primitive.Indices[triangle * 3 + corner] * 3);
            for (var axis = 0; axis < 3; axis++)
                positions[corner * 3 + axis] = MathF.Round(
                    primitive.Positions[sourceOffset + axis] * 1024f,
                    MidpointRounding.ToEven) / 1024f;
        }
        var abX = positions[3] - positions[0];
        var abY = positions[4] - positions[1];
        var abZ = positions[5] - positions[2];
        var acX = positions[6] - positions[0];
        var acY = positions[7] - positions[1];
        var acZ = positions[8] - positions[2];
        var crossX = abY * acZ - abZ * acY;
        var crossY = abZ * acX - abX * acZ;
        var crossZ = abX * acY - abY * acX;
        return crossX * crossX + crossY * crossY + crossZ * crossZ <= 0.00000001f;
    }

    private static StaticModelCanonicalAsset NormalizeOversizedSourceUnits(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken)
    {
        var maximumTriangleSpan = 0f;
        foreach (var primitive in asset.Primitives)
        {
            for (var offset = 0; offset < primitive.Indices.Length; offset += 3)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var axis = 0; axis < 3; axis++)
                {
                    var a = primitive.Positions[checked((int)primitive.Indices[offset] * 3 + axis)];
                    var b = primitive.Positions[checked((int)primitive.Indices[offset + 1] * 3 + axis)];
                    var c = primitive.Positions[checked((int)primitive.Indices[offset + 2] * 3 + axis)];
                    maximumTriangleSpan = MathF.Max(maximumTriangleSpan,
                        MathF.Max(a, MathF.Max(b, c)) - MathF.Min(a, MathF.Min(b, c)));
                }
            }
        }

        var maximumSpan = MaximumLocalCoordinate * 2f;
        if (maximumTriangleSpan <= maximumSpan) return asset;
        var decimalPlaces = checked((int)Math.Ceiling(Math.Log10(maximumTriangleSpan / maximumSpan)));
        var scale = (float)Math.Pow(0.1, decimalPlaces);
        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Static-model tfrag source scale cannot fit native position storage.");

        var primitives = asset.Primitives.Select(primitive => primitive with
        {
            Positions = primitive.Positions.Select(value => value * scale).ToArray(),
        }).ToArray();
        var bounds = asset.Bounds;
        return asset with
        {
            Primitives = primitives,
            Bounds = new(
                bounds.MinimumX * scale,
                bounds.MinimumY * scale,
                bounds.MinimumZ * scale,
                bounds.MaximumX * scale,
                bounds.MaximumY * scale,
                bounds.MaximumZ * scale),
        };
    }

    private static int RequiredPackedPartitions(
        StaticModelTfragPartition partition,
        CancellationToken cancellationToken)
    {
        var materialSlots = partition.SourceMaterialIndices
            .Select((value, index) => (value, index))
            .ToDictionary(value => value.value, value => value.index);
        var plan = UyaTfragPacketPlanner.Build(partition.Primitives.Select(primitive =>
            new UyaTfragPacketPrimitive(
                materialSlots[primitive.Geometry.MaterialIndex ?? -1],
                primitive.Geometry.Positions.Length / 3,
                primitive.Geometry.Indices)).ToArray(), cancellationToken);
        return checked((plan.EndAddress + UyaTfragPacketPlanner.VuBufferQwords - 1)
            / UyaTfragPacketPlanner.VuBufferQwords);
    }
}
