using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionSourceLinker
{
    private static readonly string[] FamilyNames = ["tfrag", "tie", "moby"];
    public static UyaOcclusionSourceLinks Link(
        UyaOcclusionMappings mappings,
        TfragTerrain? primary,
        UyaTieInstances? ties,
        UyaMobyInstances? mobys,
        IReadOnlyDictionary<int, TfragTerrain>? chunkTerrains = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        cancellationToken.ThrowIfCancellationRequested();
        var familyRows = new[] { mappings.Tfrags, mappings.Ties, mappings.Mobys };
        if (familyRows.Sum(rows => (long)rows.Count) > UyaOcclusionAnalyzer.MaximumMappingRecords
            || (long)(primary?.Chunks.Count ?? 0) + (chunkTerrains?.Values.Sum(terrain => (long)terrain.Chunks.Count) ?? 0)
                + (ties?.Instances.Count ?? 0) + (mobys?.Instances.Count ?? 0)
                > UyaOcclusionAnalyzer.MaximumMappingRecords)
            throw new InvalidDataException("UYA occlusion source links exceed the bounded record limit.");
        var links = new List<UyaOcclusionSourceLink>();
        var unresolved = new List<string>();
        var unmapped = new List<string>();
        var linked = familyRows.Select(rows => new bool[rows.Count]).ToArray();
        var triangleMatches = 0;
        var triangleMismatches = 0;
        var stashGroups = 0;
        void Add(string key, int family, int mappingIndex)
        {
            if (links.Count >= UyaOcclusionAnalyzer.MaximumMappingRecords * 2)
                throw new InvalidDataException("UYA occlusion source aliases exceed the bounded link limit.");
            var row = familyRows[family][mappingIndex];
            links.Add(new(key, FamilyNames[family], mappingIndex, row.BitIndex, row.OcclusionId));
            linked[family][mappingIndex] = true;
        }
        void LinkTerrain(TfragTerrain? terrain, string source)
        {
            foreach (var chunk in terrain?.Chunks ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = $"tfrag:{source}:group:{chunk.Index}";
                if (chunk.OcclusionIndexStash != 0)
                {
                    stashGroups++;
                    unmapped.Add(key);
                    continue;
                }
                if (chunk.OcclusionIndex < 0 || chunk.OcclusionIndex >= mappings.Tfrags.Count)
                {
                    unmapped.Add(key);
                    continue;
                }
                Add(key, 0, chunk.OcclusionIndex);
                if (mappings.Tfrags[chunk.OcclusionIndex].OcclusionId == chunk.TriangleCount) triangleMatches++;
                else triangleMismatches++;
            }
        }
        LinkTerrain(primary, "primary");
        if (chunkTerrains is not null)
            foreach (var (index, terrain) in chunkTerrains.OrderBy(pair => pair.Key))
                LinkTerrain(terrain, $"chunk:{index}");
        var tiesById = mappings.Ties.Select((row, index) => (row, index))
            .GroupBy(value => value.row.OcclusionId).ToDictionary(group => group.Key, group => group.Select(value => value.index).ToArray());
        foreach (var (instance, index) in (ties?.Instances ?? []).Select((instance, index) => (instance, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"tie:instance:{index}";
            if (!tiesById.TryGetValue(instance.OcclusionId, out var rows)) unmapped.Add(key);
            else foreach (var mappingIndex in rows) Add(key, 1, mappingIndex);
        }
        var mobysByUid = mappings.Mobys.Select((row, index) => (row, index))
            .GroupBy(value => value.row.OcclusionId).ToDictionary(group => group.Key, group => group.Select(value => value.index).ToArray());
        var flagZeroMobys = 0;
        foreach (var (instance, index) in (mobys?.Instances ?? []).Select((instance, index) => (instance, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"moby:instance:{index}";
            if (instance.Occlusion == 0) flagZeroMobys++;
            if (!mobysByUid.TryGetValue(instance.Uid, out var rows))
            {
                if (instance.Occlusion == 0) unmapped.Add(key);
            }
            else foreach (var mappingIndex in rows) Add(key, 2, mappingIndex);
        }
        for (var family = 0; family < linked.Length; family++)
        for (var index = 0; index < linked[family].Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!linked[family][index]) unresolved.Add($"{FamilyNames[family]}:mapping:{index}");
        }
        // Multiple native rows remain ambiguous until UYA runtime precedence is verified.
        var ambiguous = links.GroupBy(link => link.SourceKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).Order(StringComparer.Ordinal).ToArray();
        return new(links, unresolved, unmapped, ambiguous, primary?.TfragCount ?? 0,
            chunkTerrains?.Values.Sum(terrain => terrain.TfragCount) ?? 0, triangleMatches, triangleMismatches, stashGroups, flagZeroMobys);
    }
}
