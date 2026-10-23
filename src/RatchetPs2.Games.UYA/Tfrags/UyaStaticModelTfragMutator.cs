using System.Buffers.Binary;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static class UyaStaticModelTfragMutator
{
    public static byte[] Apply(
        byte[] terrainBytes,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> textureIdsByGroup,
        int? firstOcclusionMappingIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terrainBytes);
        ArgumentNullException.ThrowIfNull(textureIdsByGroup);
        if (firstOcclusionMappingIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(firstOcclusionMappingIndex));
        if (textureIdsByGroup.Count == 0 && firstOcclusionMappingIndex is null)
            return terrainBytes.ToArray();

        var terrain = TfragTerrainReader.Read(terrainBytes);
        var output = terrainBytes.ToArray();
        foreach (var (groupIndex, textureIds) in textureIdsByGroup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (groupIndex < 0 || groupIndex >= terrain.Chunks.Count)
                throw new InvalidDataException($"Tfrag texture remap group {groupIndex} is outside the terrain inventory.");
            var chunk = terrain.Chunks[groupIndex];
            foreach (var entry in chunk.TextureEntries)
            {
                var targetId = TargetTextureId(textureIds, entry.TextureId, groupIndex);
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(entry.Offset), targetId);
            }
            if (chunk.MSphereCount > 0
                && (chunk.MSphereOffset < 0
                    || chunk.MSphereOffset + chunk.MSphereCount * 0x10 > chunk.DataLength))
                throw new InvalidDataException($"Tfrag group {groupIndex} has an invalid mesh-sphere table.");
            for (var index = 0; index < chunk.MSphereCount; index++)
            {
                var offset = checked(chunk.DataOffset + chunk.MSphereOffset + index * 0x10 + 0x0f);
                output[offset] = checked((byte)TargetTextureId(textureIds, terrainBytes[offset], groupIndex));
            }
        }

        if (firstOcclusionMappingIndex is { } firstMappingIndex)
        {
            if (firstMappingIndex > terrain.Chunks.Count)
                throw new InvalidDataException(
                    "The first generated UYA tfrag occlusion mapping is outside the terrain inventory.");
            if (terrain.Chunks.Take(firstMappingIndex).Any(value => value.OcclusionIndex != value.Index))
                throw new InvalidDataException(
                    "Source UYA tfrag occlusion indices do not match their gameplay mapping slots.");
            if (terrain.Chunks.Count > short.MaxValue)
                throw new InvalidDataException(
                    "UYA tfrag occlusion mapping indices exceed signed 16-bit storage.");
            foreach (var chunk in terrain.Chunks.Skip(firstMappingIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                output[chunk.RecordOffset + 0x2B] = 0;
                BinaryPrimitives.WriteInt16LittleEndian(
                    output.AsSpan(chunk.RecordOffset + 0x3A), checked((short)chunk.Index));
            }
        }

        Validate(output, textureIdsByGroup, firstOcclusionMappingIndex);
        return output;
    }

    private static int TargetTextureId(
        IReadOnlyDictionary<int, int> textureIds,
        int sourceId,
        int groupIndex)
    {
        if (!textureIds.TryGetValue(sourceId, out var targetId) || targetId is < 0 or > byte.MaxValue)
            throw new InvalidDataException(
                $"Tfrag group {groupIndex} texture {sourceId} has no valid remap target.");
        return targetId;
    }

    private static void Validate(
        byte[] output,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> textureIdsByGroup,
        int? firstOcclusionMappingIndex)
    {
        var terrain = TfragTerrainReader.Read(output);
        foreach (var (groupIndex, textureIds) in textureIdsByGroup)
        {
            var targets = textureIds.Values.ToHashSet();
            var chunk = terrain.Chunks[groupIndex];
            if (chunk.TextureEntries.Any(entry => !targets.Contains(entry.TextureId))
                || Enumerable.Range(0, chunk.MSphereCount).Any(index =>
                    !targets.Contains(output[
                        chunk.DataOffset + chunk.MSphereOffset + index * 0x10 + 0x0f])))
                throw new InvalidDataException($"Tfrag group {groupIndex} texture remap failed verification.");
        }
        if (firstOcclusionMappingIndex is { } firstMappingIndex
            && terrain.Chunks.Skip(firstMappingIndex).Any(value =>
                value.OcclusionIndex != value.Index || value.OcclusionIndexStash != 0))
            throw new InvalidDataException("Generated UYA tfrag occlusion mappings failed verification.");
    }
}
