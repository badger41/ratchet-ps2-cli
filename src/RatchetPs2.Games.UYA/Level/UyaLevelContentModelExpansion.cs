using System.Buffers.Binary;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelContentModelExpansion
{
    public static LevelContentConversion ExpandTies(LevelContentConversion conversion,
        IReadOnlyDictionary<int, IReadOnlyList<int>> classes, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        ArgumentNullException.ThrowIfNull(classes);
        token.ThrowIfCancellationRequested();
        if (classes.Count == 0) return conversion;
        if (classes.Any(pair => pair.Key is < 0 or > ushort.MaxValue || pair.Value.Count < 2
            || pair.Value[0] != pair.Key || pair.Value.Any(id => id is < 0 or > ushort.MaxValue))
            || classes.Values.SelectMany(ids => ids).Distinct().Count() != classes.Values.Sum(ids => ids.Count))
            throw new ArgumentException("Split Tie classes must have unique native IDs and retain their first class.", nameof(classes));
        var blocks = conversion.Files.Where(file => file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file.Path), file => file.Bytes, StringComparer.Ordinal);
        var source = UyaTieInstancesReader.Read(blocks["tie_instances"]);
        var occupied = source.Instances.Select(tie => tie.ClassId).ToHashSet();
        if (classes.Keys.Any(id => !occupied.Contains(id))
            || classes.Values.SelectMany(ids => ids.Skip(1)).Any(occupied.Contains))
            throw new ArgumentException("Split Tie classes are missing or collide with existing instances.", nameof(classes));
        var indices = Enumerable.Range(0, source.Count).ToList();
        var targets = source.Instances.Select(tie => tie.ClassId).ToList();
        var expanded = Enumerable.Range(0, source.Count).Select(index => new List<int> { index }).ToArray();
        for (var index = 0; index < source.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!classes.TryGetValue(source.Instances[index].ClassId, out var parts)) continue;
            foreach (var part in parts.Skip(1))
            {
                expanded[index].Add(indices.Count);
                indices.Add(index); targets.Add(part);
            }
        }
        if (indices.Count > short.MaxValue) throw new InvalidDataException("Split Tie instances exceed the native ambient index range.");
        var records = new byte[checked(0x10 + indices.Count * UyaTieInstancesReader.RecordSize + source.TrailingBytes.Length)];
        blocks["tie_instances"].AsSpan(0, 0x10).CopyTo(records);
        BinaryPrimitives.WriteInt32LittleEndian(records, indices.Count);
        for (var index = 0; index < indices.Count; index++)
        {
            var record = records.AsSpan(0x10 + index * UyaTieInstancesReader.RecordSize, UyaTieInstancesReader.RecordSize);
            source.Instances[indices[index]].RawBytes.CopyTo(record);
            BinaryPrimitives.WriteInt32LittleEndian(record, targets[index]);
        }
        source.TrailingBytes.CopyTo(records, 0x10 + indices.Count * UyaTieInstancesReader.RecordSize);
        blocks["tie_instances"] = records;
        var colors = UyaGameplayLightingReader.ReadTieAmbientRgbas(blocks["tie_ambient_rgbas"], source.Count);
        blocks["tie_ambient_rgbas"] = UyaTieAmbientRgbasWriter.Write(indices.Select(index => colors[index]).ToArray());
        if (blocks["tie_groups"].Length > 0)
        {
            var groups = UyaTieGroupsReader.Read(blocks["tie_groups"]).Groups;
            if (groups.SelectMany(group => group).Any(index => index >= source.Count))
                throw new InvalidDataException("Split Tie group references a missing source instance.");
            blocks["tie_groups"] = UyaTieGroupsWriter.Write(groups.Select(group =>
                (IReadOnlyList<int>)group.SelectMany(index => expanded[index]).ToArray()).ToArray());
        }
        if (blocks["occlusion"].Length > 0)
            blocks["occlusion"] = UyaOcclusionMappingsWriter.RemapTiesBySourceIndex(blocks["occlusion"], indices,
                indices.Select(index => source.Instances[index].OcclusionId).ToArray());
        if (blocks["tie_classes"].Length > 0)
            blocks["tie_classes"] = UyaClassIdListWriter.Compose(blocks["tie_classes"], targets);
        token.ThrowIfCancellationRequested();
        var gameplay = UyaConvertedLevelPackageBuilder.Gameplay(blocks);
        var replaced = gameplay.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        return conversion with { Files = conversion.Files.Where(file => !replaced.Contains(file.Path)).Concat(gameplay).ToArray() };
    }
}
