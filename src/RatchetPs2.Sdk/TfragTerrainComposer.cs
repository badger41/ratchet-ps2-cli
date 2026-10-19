using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Tfrags;

namespace RatchetPs2.Sdk;

public sealed record TfragOcclusionComposition(
    byte[] OcclusionBytes,
    int? FirstCustomMappingIndex,
    int? VisibilityBit,
    bool Changed);

public static class TfragTerrainComposer
{
    public static byte[] AppendTerrain(
        GameId gameId,
        byte[] sourceTerrainBytes,
        IReadOnlyList<StaticModelTfragPiece> pieces,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragWriter.Append(sourceTerrainBytes, pieces, cancellationToken),
        _ => throw new NotSupportedException($"Tfrag terrain appending does not support {gameId}."),
    };

    public static byte[] RemapTerrainTextures(
        GameId gameId,
        byte[] terrainBytes,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> textureIdsByGroup,
        CancellationToken cancellationToken = default) => FinalizeTerrain(
            gameId, terrainBytes, textureIdsByGroup, firstOcclusionMappingIndex: null, cancellationToken);

    public static byte[] FinalizeTerrain(
        GameId gameId,
        byte[] terrainBytes,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> textureIdsByGroup,
        int? firstOcclusionMappingIndex,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragMutator.Apply(
            terrainBytes, textureIdsByGroup, firstOcclusionMappingIndex, cancellationToken),
        _ => throw new NotSupportedException($"Tfrag terrain finalization does not support {gameId}."),
    };

    public static byte[] FinalizeTerrain(
        GameId gameId,
        byte[] terrainBytes,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> textureAssetIdsByGroup,
        IReadOnlyList<TerrainTextureAllocation> terrainTextures,
        int? firstOcclusionMappingIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textureAssetIdsByGroup);
        ArgumentNullException.ThrowIfNull(terrainTextures);
        var textureIdByAsset = terrainTextures.ToDictionary(
            value => value.AssetId, value => value.TextureId, StringComparer.Ordinal);
        var textureIdsByGroup = textureAssetIdsByGroup.ToDictionary(
            value => value.Key,
            value => (IReadOnlyDictionary<int, int>)value.Value.ToDictionary(
                binding => binding.Key,
                binding => textureIdByAsset.TryGetValue(binding.Value, out var textureId)
                    ? textureId
                    : throw new InvalidDataException(
                        $"Tfrag texture {binding.Value} has no terrain texture allocation.")));
        return FinalizeTerrain(
            gameId, terrainBytes, textureIdsByGroup, firstOcclusionMappingIndex, cancellationToken);
    }

    public static byte[] BindMissingOcclusionMappings(
        GameId gameId,
        byte[] terrainBytes,
        int firstMappingIndex,
        CancellationToken cancellationToken = default) => FinalizeTerrain(
            gameId,
            terrainBytes,
            new Dictionary<int, IReadOnlyDictionary<int, int>>(),
            firstMappingIndex,
            cancellationToken);

    public static byte[] FilterTerrainGroups(
        GameId gameId,
        byte[] terrainBytes,
        IReadOnlyList<int> retainedGroupIndices,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTfragWriter.Filter(
            terrainBytes, retainedGroupIndices, cancellationToken),
        _ => throw new NotSupportedException($"Tfrag terrain filtering does not support {gameId}."),
    };

    public static TfragOcclusionComposition ComposeOcclusionMappings(
        GameId gameId,
        byte[] terrainBytes,
        byte[] sourceTerrainBytes,
        byte[] occlusionBytes,
        int retainedNativeGroupCount,
        int? visibilityBit,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => ComposeUyaOcclusionMappings(
            terrainBytes,
            sourceTerrainBytes,
            occlusionBytes,
            retainedNativeGroupCount,
            visibilityBit,
            cancellationToken),
        _ => throw new NotSupportedException($"Tfrag occlusion composition does not support {gameId}."),
    };

    private static TfragOcclusionComposition ComposeUyaOcclusionMappings(
        byte[] terrainBytes,
        byte[] sourceTerrainBytes,
        byte[] occlusionBytes,
        int retainedNativeGroupCount,
        int? visibilityBit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainBytes);
        ArgumentNullException.ThrowIfNull(sourceTerrainBytes);
        ArgumentNullException.ThrowIfNull(occlusionBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var sourceTerrain = sourceTerrainBytes.Length == 0 || !sourceTerrainBytes.AsSpan().ContainsAnyExcept((byte)0)
            ? null
            : TfragTerrainReader.Read(sourceTerrainBytes);
        var terrain = TfragTerrainReader.Read(terrainBytes);
        var sourceGroupCount = sourceTerrain?.TfragCount ?? 0;
        var customGroupCount = terrain.TfragCount - retainedNativeGroupCount;
        if (customGroupCount < 0)
            throw new InvalidDataException(
                "Packed terrain contains fewer groups than its retained source inventory.");
        if (retainedNativeGroupCount != 0 && retainedNativeGroupCount != sourceGroupCount)
            throw new InvalidDataException(
                "Terrain composition supports retaining all or deleting all native primary groups.");

        var mappings = UyaOcclusionMappingsReader.Read(occlusionBytes);
        if (mappings.Tfrags.Count != sourceGroupCount)
            throw new InvalidDataException(
                "UYA gameplay occlusion mappings do not match the source tfrag inventory.");
        var output = retainedNativeGroupCount == 0
            ? UyaOcclusionMappingsWriter.FilterTfrags(occlusionBytes, [])
            : occlusionBytes.ToArray();
        var retainedMappings = UyaOcclusionMappingsReader.Read(output);
        if (customGroupCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceTerrain is not null)
            {
                var appended = UyaOcclusionMappingsWriter.MapTfragsByNearestSource(
                    sourceTerrain.Chunks,
                    mappings.Tfrags,
                    terrain.Chunks.Skip(retainedNativeGroupCount).ToArray());
                output = UyaOcclusionMappingsWriter.AppendTfrags(output, appended);
            }
            else
            {
                visibilityBit ??= Enumerable.Range(0, 1024).FirstOrDefault(index =>
                    !retainedMappings.Tfrags.Concat(retainedMappings.Ties).Concat(retainedMappings.Mobys)
                        .Any(value => value.BitIndex == index), -1);
                if (visibilityBit < 0)
                    throw new InvalidDataException(
                        "UYA occlusion mapping has no free visibility bit for custom tfrags.");
                output = UyaOcclusionMappingsWriter.AppendTfrags(
                    output, customGroupCount, visibilityBit.Value, 0);
            }
        }

        return new(
            output,
            customGroupCount > 0 ? retainedMappings.Tfrags.Count : null,
            visibilityBit,
            retainedNativeGroupCount != sourceGroupCount || customGroupCount > 0);
    }
}
