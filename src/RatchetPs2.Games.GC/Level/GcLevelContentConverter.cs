using System.Buffers.Binary;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.GC.Gameplay;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.GC.Level;

public static class GcLevelContentConverter
{
    public static LevelContentConversion ConvertToUya(byte[] levelWad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levelWad);
        cancellationToken.ThrowIfCancellationRequested();
        var header = UyaLevelWadReader.ReadLevelWad(levelWad);
        var files = UyaLevelWadUnpacker.UnpackLevelData(
            UyaLevelWadReader.ReadSectorFileBlock(levelWad, header.Data)).ToList();
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(files);
        var assetHeader = LevelAssetReader.ReadHeader(source.HeaderBytes);
        var assetBytes = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes, new(), cancellationToken) : source.AssetWadBytes;
        var assetFileIndex = files.FindIndex(file => file.Path == "assets/asset_wad.bin");
        files[assetFileIndex] = files[assetFileIndex] with { Bytes = assetBytes };
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(assetHeader, assetBytes.Length,
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, assetHeader.MobyModelOffset, assetHeader.MobyModelCount),
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, assetHeader.TieModelOffset, assetHeader.TieModelCount),
            LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, assetHeader.ShrubModelOffset, assetHeader.ShrubModelCount),
            [assetHeader.SceneViewSize]);
        var terrain = LevelAssetReader.ReadAssetSlice(assetBytes, assetHeader.TerrainOffset, offsets, allowZeroOffset: true);
        var terrainMappingCount = terrain.Length > 0 && terrain.AsSpan().ContainsAnyExcept((byte)0)
            ? TfragTerrainReader.Read(terrain).TotalTfragCount : 0;
        if (terrainMappingCount is < 0 or > 100_000)
            throw new InvalidDataException("GC terrain mapping inventory exceeds the item limit.");
        var stored = UyaLevelWadReader.ReadSectorFileBlock(levelWad, header.Gameplay);
        var raw = BinaryMagic.IsWad(stored) ? WadCompression.Decompress(stored, new(), cancellationToken) : stored;
        var blocks = GameplayLayoutReader.Read(raw, GcGameplayLayout.Core).Blocks;
        var nativeBlocks = new Dictionary<string, byte[]>();
        foreach (var definition in UyaGameplayLayout.Core.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = blocks.Single(block => block.SemanticName == definition.SemanticName).PayloadBytes;
            if (definition.SemanticName == "level_settings" && bytes.Length > 0)
            {
                _ = GcLevelSettingsReader.Read(bytes);
                var converted = new byte[UyaLevelSettingsReader.MinimumSize];
                bytes.AsSpan(0, GcLevelSettingsReader.MinimumSize).CopyTo(converted);
                foreach (var offset in new[] { 0x4c, 0x50, 0x54 })
                    BinaryPrimitives.WriteInt32LittleEndian(converted.AsSpan(offset), -1);
                bytes = converted;
            }
            if (definition.SemanticName == "occlusion" && bytes.Length > 0)
            {
                var tieBytes = blocks.Single(block => block.SemanticName == "tie_instances").PayloadBytes;
                IReadOnlyList<UyaTieInstance> ties = tieBytes.Length == 0 ? [] : UyaTieInstancesReader.Read(tieBytes).Instances;
                bytes = UyaOcclusionMappingsWriter.NormalizeReplacement(bytes, terrainMappingCount, ties);
            }
            // These GC behavior and environment records are retained in RawGameplay, not translated into UYA records.
            if (definition.SemanticName is "cameras" or "sound_instances" or "point_lights"
                or "env_transitions" or "env_sample_points") bytes = [];
            if (definition.SemanticName is "tie_instances" or "shrub_instances" or "moby_instances"
                && bytes.Length == 0) bytes = new byte[0x10];
            nativeBlocks.Add(definition.SemanticName, bytes);
        }
        files.AddRange(UyaConvertedLevelPackageBuilder.Gameplay(nativeBlocks, alignment: 4));
        // Slot zero is the resident terrain buffer already represented by the primary assets.
        for (var index = 1; index < header.Chunks.Count; index++)
        {
            var bytes = UyaLevelWadReader.ReadSectorFileBlock(levelWad, header.Chunks[index]);
            if (bytes.Length > 0) files.Add(new($"level_wad/chunks/chunk{index}.wad", bytes, "application/octet-stream"));
        }
        var models = LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, assetHeader.MobyModelOffset, assetHeader.MobyModelCount)
            .Where(definition => definition.ModelOffset > 0)
            .Select(definition => new LevelContentModelUse(FrontendAssetKind.Moby, definition.ModelId, definition.Index))
            .Concat(LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, assetHeader.TieModelOffset, assetHeader.TieModelCount)
                .Where(definition => definition.ModelOffset > 0)
                .Select(definition => new LevelContentModelUse(FrontendAssetKind.Tie, definition.ModelId, definition.Index)))
            .Concat(LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, assetHeader.ShrubModelOffset, assetHeader.ShrubModelCount)
                .Where(definition => definition.ModelOffset > 0)
                .Select(definition => new LevelContentModelUse(FrontendAssetKind.Shrub, definition.ModelId, definition.Index))).ToArray();
        return new(files, raw, GcSkyRotationReader.ReadRadiansPerFrame(source.CodeBytes).ToDictionary(pair => pair.Key, pair => pair.Value * 60), header.Chunks.Count, models, PresentGameplaySections: blocks.Where(block => block.Pointer > 0).Select(block => block.SemanticName).ToHashSet(StringComparer.Ordinal), HasSceneSettings: false);
    }
}
