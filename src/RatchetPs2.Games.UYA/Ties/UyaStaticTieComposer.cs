using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Textures;

namespace RatchetPs2.Games.UYA.Ties;

internal static partial class UyaStaticTieComposer
{
    public static StaticModelNativeConversionResult Convert(
        StaticModelCanonicalAsset asset,
        int targetClassId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = Prepare(asset, targetClassId, cancellationToken);
        var textureResult = UyaStaticModelTextureConverter.Build(
            asset, prepared.Materials, prepared.Diagnostics, int.MaxValue, cancellationToken);
        var binary = UyaStaticTieBinaryWriter.Write(prepared, textureResult.Materials, cancellationToken);
        var definition = new byte[0x20];
        BinaryPrimitives.WriteInt32LittleEndian(definition.AsSpan(4), targetClassId);
        definition.AsSpan(0x10, 0x10).Fill(byte.MaxValue);

        UyaStaticTieBinaryWriter.Validate(binary, prepared, textureResult.Materials);
        return new StaticModelNativeConversionResult(
            GameId.UYA,
            targetClassId,
            definition,
            binary.ModelBytes,
            Hash(binary.ModelBytes),
            textureResult.Textures,
            textureResult.Materials.Select(material => new StaticModelNativeMaterialBinding(
                material.NativeIndex,
                material.SourceIndex,
                material.Texture.AssetId,
                material.ClampU,
                material.ClampV)).ToArray(),
            binary.PrimitiveBindings,
            textureResult.Materials.Select(material => material.Texture.PifBytes).ToArray(),
            prepared.Diagnostics,
            "lod0-only");
    }

    private static PreparedModel Prepare(
        StaticModelCanonicalAsset asset,
        int targetClassId,
        CancellationToken cancellationToken)
    {
        if (asset.FormatVersion != StaticModelCanonicalFormat.Version)
            throw new InvalidDataException($"Unsupported canonical static-model version {asset.FormatVersion}.");
        if (asset.Primitives is null || asset.Samplers is null || asset.Textures is null
            || asset.Materials is null || asset.Images is null)
            throw new InvalidDataException("The canonical static model has a missing collection.");
        if (targetClassId is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(targetClassId), "UYA TIE class IDs must fit in 16 bits.");
        if (asset.Primitives.Count == 0)
            throw new InvalidDataException("The canonical static model has no primitives.");
        foreach (var primitive in asset.Primitives)
            ValidatePrimitive(primitive, asset.Materials.Count);
        ValidateBounds(asset);
        var triangleCount = asset.Primitives.Sum(primitive => primitive.Indices.Length / 3);
        if (triangleCount > UyaTriangleStripBuilder.MaximumSourceTriangles)
            throw new InvalidDataException(
                $"UYA TIE conversion supports at most {UyaTriangleStripBuilder.MaximumSourceTriangles} "
                + $"source triangles before packet construction; the source needs {triangleCount}.");

        var diagnostics = new List<StaticModelNativeDiagnostic>();
        var sourceMaterials = asset.Primitives.Select(value => value.MaterialIndex)
            .Distinct()
            .OrderBy(value => value ?? int.MaxValue)
            .ToArray();
        if (sourceMaterials.Length > StaticModelNativeLimits.MaximumMaterials)
            throw new InvalidDataException(
                $"UYA TIE models support at most {StaticModelNativeLimits.MaximumMaterials} referenced materials; the source uses {sourceMaterials.Length}.");
        var materialIndexes = sourceMaterials.Select((value, index) => (Key: value ?? -1, Index: index))
            .ToDictionary(value => value.Key, value => value.Index);
        var materials = UyaStaticModelTextureConverter.PrepareMaterials(asset, diagnostics);

        var primitives = new List<PreparedPrimitive>(asset.Primitives.Count);
        foreach (var primitive in asset.Primitives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var materialIndex = materialIndexes[primitive.MaterialIndex ?? -1];
            var preparedPrimitive = WeldIdenticalVertices(
                RebaseRepeatTexCoords(primitive, materials[materialIndex]),
                cancellationToken);
            var packets = UyaTriangleStripBuilder.Build(preparedPrimitive, cancellationToken);
            primitives.Add(new PreparedPrimitive(preparedPrimitive, materialIndex, packets));
        }

        var (nativeTriangleCount, stripCount, logicalVertexCount, packetCount) = PackingMetrics(primitives);
        var remapGroupCount = UyaStaticTieBinaryWriter.CountRemapGroups(PacketVertexCounts(primitives));
        if (packetCount > UyaStaticTieBinaryWriter.MaximumPackets
            || remapGroupCount > UyaStaticTieBinaryWriter.MaximumRemapGroups)
        {
            for (var index = 0; index < primitives.Count; index++)
            {
                var primitive = primitives[index];
                primitives[index] = primitive with
                {
                    Packets = UyaTriangleStripBuilder.Build(
                        primitive.Source,
                        cancellationToken,
                        UyaTriangleStripBuilder.CompactMaximumStripVertices),
                };
            }
            (nativeTriangleCount, stripCount, logicalVertexCount, packetCount) = PackingMetrics(primitives);
            remapGroupCount = UyaStaticTieBinaryWriter.CountRemapGroups(PacketVertexCounts(primitives));
        }
        if (remapGroupCount > UyaStaticTieBinaryWriter.MaximumRemapGroups)
        {
            var groupPacking = new UyaRemapGroupPackingState();
            for (var index = 0; index < primitives.Count; index++)
            {
                var primitive = primitives[index];
                primitives[index] = primitive with
                {
                    Packets = UyaTriangleStripBuilder.RepackForRemapGroups(primitive.Packets, groupPacking),
                };
            }
            packetCount = primitives.Sum(primitive => primitive.Packets.Count);
            remapGroupCount = UyaStaticTieBinaryWriter.CountRemapGroups(PacketVertexCounts(primitives));
            if (remapGroupCount > UyaStaticTieBinaryWriter.MaximumRemapGroups)
                throw new InvalidDataException(
                    $"UYA TIE normal/RGBA remaps need {remapGroupCount} groups, over the "
                    + $"{UyaStaticTieBinaryWriter.MaximumRemapGroups}-group cache limit.");
        }

        if (packetCount > UyaStaticTieBinaryWriter.MaximumPackets)
            throw new InvalidDataException(
                $"UYA TIE LOD tables support at most {UyaStaticTieBinaryWriter.MaximumPackets} packets; the source needs {packetCount}.");
        return new PreparedModel(targetClassId, primitives, materials, triangleCount, nativeTriangleCount, stripCount,
            logicalVertexCount, packetCount, diagnostics);

        static IEnumerable<int> PacketVertexCounts(IEnumerable<PreparedPrimitive> values) =>
            values.SelectMany(primitive => primitive.Packets)
                .Select(packet => packet.Strips.Sum(strip => strip.Vertices.Count));

        static (int NativeTriangles, int Strips, int LogicalVertices, int Packets) PackingMetrics(
            IEnumerable<PreparedPrimitive> values)
        {
            var packets = values.SelectMany(primitive => primitive.Packets).ToArray();
            return (
                packets.Sum(packet => packet.Strips.Sum(strip => strip.Vertices.Count - 2)),
                packets.Sum(packet => packet.Strips.Count),
                packets.Sum(packet => packet.Strips.Sum(strip => strip.Vertices.Count)),
                packets.Length);
        }
    }

    private static StaticModelPrimitive RebaseRepeatTexCoords(
        StaticModelPrimitive primitive,
        PreparedMaterial material)
    {
        if (primitive.TexCoords is null) return primitive;
        var texCoords = primitive.TexCoords.ToArray();
        RebaseAxis(0, material.ClampU);
        RebaseAxis(1, material.ClampV);
        return primitive with { TexCoords = texCoords };

        void RebaseAxis(int axis, bool clamp)
        {
            if (clamp) return;
            var minimum = Enumerable.Range(0, texCoords.Length / 2)
                .Min(index => texCoords[index * 2 + axis]);
            var offset = MathF.Floor(minimum);
            if (offset == 0) return;
            for (var index = axis; index < texCoords.Length; index += 2)
                texCoords[index] -= offset;
        }
    }

    private static StaticModelPrimitive WeldIdenticalVertices(
        StaticModelPrimitive primitive,
        CancellationToken cancellationToken)
    {
        var representatives = new Dictionary<StaticVertexKey, uint>();
        var indices = primitive.Indices.ToArray();
        var changed = false;
        for (var indexOffset = 0; indexOffset < indices.Length; indexOffset++)
        {
            if ((indexOffset & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var sourceIndex = checked((int)indices[indexOffset]);
            var positionOffset = sourceIndex * 3;
            var texCoordOffset = sourceIndex * 2;
            var key = new StaticVertexKey(
                primitive.Positions[positionOffset],
                primitive.Positions[positionOffset + 1],
                primitive.Positions[positionOffset + 2],
                primitive.Normals?[positionOffset] ?? 0f,
                primitive.Normals?[positionOffset + 1] ?? 0f,
                primitive.Normals?[positionOffset + 2] ?? 0f,
                primitive.TexCoords?[texCoordOffset] ?? 0f,
                primitive.TexCoords?[texCoordOffset + 1] ?? 0f);
            if (representatives.TryGetValue(key, out var representative))
            {
                changed |= representative != indices[indexOffset];
                indices[indexOffset] = representative;
            }
            else
            {
                representatives.Add(key, indices[indexOffset]);
            }
        }
        return changed ? primitive with { Indices = indices } : primitive;
    }

    private static void ValidatePrimitive(StaticModelPrimitive primitive, int materialCount)
    {
        var context = $"mesh {primitive.SourceMeshIndex}, primitive {primitive.SourcePrimitiveIndex}";
        if (primitive.SourceMeshIndex < 0 || primitive.SourcePrimitiveIndex < 0)
            throw new InvalidDataException($"Source {context} has a negative source index.");
        if (primitive.MaterialIndex is { } materialIndex && (materialIndex < 0 || materialIndex >= materialCount))
            throw new InvalidDataException($"Source {context} references missing material {materialIndex}.");
        if (primitive.Positions is null || primitive.Positions.Length == 0 || primitive.Positions.Length % 3 != 0
            || primitive.Positions.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"Source {context} has an invalid position array.");
        var vertexCount = primitive.Positions.Length / 3;
        if (primitive.Indices is null || primitive.Indices.Length == 0 || primitive.Indices.Length % 3 != 0
            || primitive.Indices.Any(index => index >= vertexCount))
            throw new InvalidDataException($"Source {context} has invalid triangle indices.");
        if (primitive.Normals is { } normals
            && (normals.Length != primitive.Positions.Length || normals.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException($"Source {context} has an invalid normal array.");
        if (primitive.TexCoords is { } texCoords
            && (texCoords.Length != vertexCount * 2 || texCoords.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException($"Source {context} has an invalid texture-coordinate array.");
        if (primitive.Colors is { } colors)
        {
            if (colors.Length != vertexCount * 4 || colors.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException($"Source {context} has an invalid color array.");
            if (colors.Any(value => MathF.Abs(value - 1f) > 0.0001f))
                throw new InvalidDataException($"Source {context} uses vertex colors, which the initial UYA TIE converter cannot preserve.");
        }
    }

    private static void ValidateBounds(StaticModelCanonicalAsset asset)
    {
        var bounds = asset.Bounds;
        var values = new[]
        {
            bounds.MinimumX, bounds.MinimumY, bounds.MinimumZ,
            bounds.MaximumX, bounds.MaximumY, bounds.MaximumZ,
        };
        if (values.Any(value => !float.IsFinite(value))
            || bounds.MinimumX > bounds.MaximumX
            || bounds.MinimumY > bounds.MaximumY
            || bounds.MinimumZ > bounds.MaximumZ)
            throw new InvalidDataException("The canonical static model has invalid bounds.");
        const float tolerance = 0.0001f;
        foreach (var primitive in asset.Primitives)
        {
            for (var offset = 0; offset < primitive.Positions.Length; offset += 3)
            {
                if (primitive.Positions[offset] < bounds.MinimumX - tolerance
                    || primitive.Positions[offset] > bounds.MaximumX + tolerance
                    || primitive.Positions[offset + 1] < bounds.MinimumY - tolerance
                    || primitive.Positions[offset + 1] > bounds.MaximumY + tolerance
                    || primitive.Positions[offset + 2] < bounds.MinimumZ - tolerance
                    || primitive.Positions[offset + 2] > bounds.MaximumZ + tolerance)
                    throw new InvalidDataException(
                        $"Source mesh {primitive.SourceMeshIndex}, primitive {primitive.SourcePrimitiveIndex} lies outside the canonical bounds.");
            }
        }
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => System.Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed record PreparedModel(
    int TargetClassId,
    IReadOnlyList<PreparedPrimitive> Primitives,
    IReadOnlyList<PreparedMaterial> Materials,
    int TriangleCount,
    int NativeTriangleCount,
    int StripCount,
    int LogicalVertexCount,
    int PacketCount,
    List<StaticModelNativeDiagnostic> Diagnostics);

internal sealed record PreparedPrimitive(
    StaticModelPrimitive Source,
    int MaterialIndex,
    IReadOnlyList<UyaTrianglePacket> Packets);

internal readonly record struct StaticVertexKey(
    float X,
    float Y,
    float Z,
    float NormalX,
    float NormalY,
    float NormalZ,
    float U,
    float V);
