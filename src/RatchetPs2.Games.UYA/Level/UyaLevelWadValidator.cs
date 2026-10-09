using System.Buffers.Binary;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadValidator
{
    public static void Validate(UyaLevelWadInventory inventory, UyaLevelWadPackage package)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(package);
        ValidateAssetSizes(inventory);
        ValidateStaticReferences(package);
    }

    public static bool EquivalentAssetHeader(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        expected.Length >= 0x90
        && actual.Length >= expected.Length
        && actual[..0x88].SequenceEqual(expected[..0x88])
        && actual[0x90..expected.Length].SequenceEqual(expected[0x90..])
        && actual[expected.Length..].ContainsAnyExcept((byte)0) == false;

    private static void ValidateAssetSizes(UyaLevelWadInventory inventory)
    {
        var levelData = inventory.Containers.Single(value => value.Path == "level_wad/level_data.wad");
        var header = levelData.Slots.Single(value => value.Path == "assets/asset_header.bin").Bytes.Span;
        var encoded = levelData.Slots.Single(value => value.Path == "assets/asset_wad.bin");
        var decoded = inventory.Containers.Single(value => value.Path == "assets/asset_wad.bin");
        if (header.Length < 0x90
            || BinaryPrimitives.ReadInt32LittleEndian(header[0x88..]) != encoded.Length
            || BinaryPrimitives.ReadInt32LittleEndian(header[0x8c..]) != decoded.Bytes.Length)
            throw new InvalidDataException("Packed asset header does not match the asset WAD sizes.");
    }

    private static void ValidateStaticReferences(UyaLevelWadPackage package)
    {
        var files = package.Files.ToDictionary(value => value.Path, StringComparer.Ordinal);
        if (files.TryGetValue("gameplay/core/tie_instances.bin", out var ties))
        {
            var tieInstances = UyaTieInstancesReader.Read(ties.Bytes);
            var tieCount = tieInstances.Count;
            var tieClasses = tieInstances.Instances.Select(value => value.ClassId).ToArray();
            if (!tieClasses.SequenceEqual(tieClasses.Order()))
                throw new InvalidDataException("Packed tie instances are not in ascending class blocks.");
            if (files.TryGetValue("gameplay/core/tie_groups.bin", out var groups)
                && UyaTieGroupsReader.Read(groups.Bytes).Groups.SelectMany(value => value)
                    .Any(value => value >= tieCount))
                throw new InvalidDataException("Packed tie groups reference a missing tie instance.");
            if (files.TryGetValue("gameplay/core/occlusion.bin", out var occlusion))
            {
                var tieMappings = UyaOcclusionMappingsReader.Read(occlusion.Bytes).Ties;
                if (tieMappings.Count != tieCount)
                    throw new InvalidDataException("Packed tie occlusion mapping count does not match the tie instance count.");
                if (!tieMappings.Select(value => value.OcclusionId).Order()
                        .SequenceEqual(tieInstances.Instances.Select(value => value.OcclusionId).Order()))
                    throw new InvalidDataException("Packed tie occlusion IDs do not match their instances.");
            }
        }

        if (!files.TryGetValue("gameplay/core/shrub_instances.bin", out var shrubs)) return;
        var shrubInstances = UyaShrubInstancesReader.Read(shrubs.Bytes);
        var shrubCount = shrubInstances.Count;
        var shrubClasses = shrubInstances.Instances.Select(value => value.ClassId).ToArray();
        if (!shrubClasses.SequenceEqual(shrubClasses.Order()))
            throw new InvalidDataException("Packed shrub instances are not in ascending class blocks.");
        if (files.TryGetValue("gameplay/core/shrub_groups.bin", out var shrubGroups)
            && UyaTieGroupsReader.Read(shrubGroups.Bytes).Groups.SelectMany(value => value)
                .Any(value => value >= shrubCount))
            throw new InvalidDataException("Packed shrub groups reference a missing shrub instance.");
    }
}
