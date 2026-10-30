using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionLevelAnalyzer
{
    public const int MaximumLevelWadBytes = 256 * 1024 * 1024;

    public static UyaOcclusionLevelAnalysis Analyze(
        ReadOnlySpan<byte> levelWadBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (levelWadBytes.Length > MaximumLevelWadBytes)
            throw new InvalidDataException("UYA level WAD exceeds the bounded occlusion analysis limit.");
        var files = UyaLevelWadUnpacker.Unpack(levelWadBytes).Files.ToDictionary(file => file.Path, file => file.Bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var headerBytes = files["assets/asset_header.bin"];
        if (headerBytes.Length < 0xc0)
            throw new InvalidDataException("UYA occlusion asset header is truncated.");
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var external = files.GetValueOrDefault("occlusion/occlusion.bin") ?? [];
        if (header.OcclusionOffset <= 0)
            return new("NoAssetGrid", null, null, external.Length, UyaOcclusionAnalyzer.Hash(external), null,
                header.OcclusionOctreeOffset, header.OcclusionRadiusOffset, header.OcclusionRadius2Offset, []);
        foreach (var (offset, count, stride) in new[] {
            (header.MobyModelOffset, header.MobyModelCount, 0x20),
            (header.TieModelOffset, header.TieModelCount, 0x20),
            (header.ShrubModelOffset, header.ShrubModelCount, 0x30) })
            if (count < 0 || count > UyaOcclusionAnalyzer.MaximumMappingRecords
                || (count != 0 && (offset < 0 || (long)offset + (long)count * stride > headerBytes.Length)))
                throw new InvalidDataException("UYA occlusion asset model table exceeds its source or analysis limit.");
        var sourceAssets = files["assets/asset_wad.bin"];
        var assets = BinaryMagic.IsWad(sourceAssets)
            ? WadCompression.Decompress(sourceAssets, new(), cancellationToken) : sourceAssets;
        var mobyModels = LevelAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var tieModels = LevelAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubModels = LevelAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(header, assets.Length, mobyModels, tieModels, shrubModels, [header.SceneViewSize]);
        var gridBytes = LevelAssetReader.ReadAssetSlice(assets, header.OcclusionOffset, offsets);
        var mappingBytes = files.GetValueOrDefault("gameplay/core/occlusion.bin")
            ?? throw new InvalidDataException("UYA occlusion grid has no gameplay mapping table.");
        var analysis = UyaOcclusionAnalyzer.Analyze(gridBytes, mappingBytes, cancellationToken);
        var mappings = UyaOcclusionMappingsReader.Read(mappingBytes, UyaOcclusionAnalyzer.MaximumMappingRecords, cancellationToken);
        var recordBytes = checked(16 + (mappings.Tfrags.Count + mappings.Ties.Count + mappings.Mobys.Count) * 8);
        var externalMatches = external.Length >= recordBytes
            && external.AsSpan(0, recordBytes).SequenceEqual(mappingBytes.AsSpan(0, recordBytes));
        var terrainBytes = LevelAssetReader.ReadAssetSlice(assets, header.TerrainOffset, offsets, allowZeroOffset: true);
        TfragTerrain? terrain = null;
        if (terrainBytes.AsSpan().ContainsAnyExcept((byte)0))
        {
            if (terrainBytes.Length < 8 || System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(terrainBytes.AsSpan(4))
                > UyaOcclusionAnalyzer.MaximumMappingRecords)
                throw new InvalidDataException("UYA terrain exceeds the bounded occlusion analysis record limit.");
            terrain = TfragTerrainReader.Read(terrainBytes);
        }
        var ties = ReadTies(files.GetValueOrDefault("gameplay/core/tie_instances.bin"));
        var mobys = ReadMobys(files.GetValueOrDefault("gameplay/core/moby_instances.bin"));
        var chunks = new Dictionary<int, TfragTerrain>();
        for (var index = 1; index < 3; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!files.TryGetValue($"level_wad/chunks/chunk{index}.wad", out var chunk)) continue;
            var chunkBytes = TfragChunkWadReader.ReadTerrainPayload(chunk);
            if (!chunkBytes.AsSpan().ContainsAnyExcept((byte)0)) continue;
            if (chunkBytes.Length < 8 || System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(chunkBytes.AsSpan(4))
                > UyaOcclusionAnalyzer.MaximumMappingRecords)
                throw new InvalidDataException("UYA chunk terrain exceeds the bounded occlusion analysis record limit.");
            chunks.Add(index, TfragTerrainReader.Read(chunkBytes));
        }
        var links = UyaOcclusionSourceLinker.Link(mappings, terrain, ties, mobys, chunks, cancellationToken);
        return new("Read", analysis, links, external.Length, UyaOcclusionAnalyzer.Hash(external), externalMatches,
            header.OcclusionOctreeOffset, header.OcclusionRadiusOffset, header.OcclusionRadius2Offset, chunks.Keys.Order().ToArray());
    }

    private static void CheckCount(byte[] bytes)
    {
        if (bytes.Length < 4 || System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes)
            > UyaOcclusionAnalyzer.MaximumMappingRecords)
            throw new InvalidDataException("UYA instances exceed the bounded occlusion analysis record limit.");
    }

    private static UyaTieInstances? ReadTies(byte[]? bytes)
    {
        if (bytes is null) return null;
        CheckCount(bytes);
        return UyaTieInstancesReader.Read(bytes);
    }

    private static UyaMobyInstances? ReadMobys(byte[]? bytes)
    {
        if (bytes is null) return null;
        CheckCount(bytes);
        return UyaMobyInstancesReader.Read(bytes);
    }
}
