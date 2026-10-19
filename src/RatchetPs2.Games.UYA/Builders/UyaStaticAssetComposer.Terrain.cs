using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.UYA.Builders;

internal static partial class UyaStaticAssetComposer
{
    public static StaticAssetComposition AppendTerrainTextures(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        ReadOnlySpan<byte> paletteBytes,
        IReadOnlyList<TerrainTextureInput> terrainTextures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terrainTextures);
        if (headerBytes.Length < HeaderFixedSize)
            throw new InvalidDataException("UYA asset header is shorter than its fixed header.");
        var ordered = terrainTextures.Select(ValidateTerrainTexture)
            .OrderBy(value => value.AssetId, StringComparer.Ordinal).ToArray();
        var duplicate = ordered.GroupBy(value => value.AssetId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate terrain texture asset {duplicate.Key}.", nameof(terrainTextures));

        var sourceHeader = LevelAssetReader.ReadHeader(headerBytes);
        var terrainDefinitions = LevelAssetReader.ReadTextureDefinitions(
                headerBytes, sourceHeader.TerrainTextureOffset, sourceHeader.TerrainTextureCount)
            .Select(value => new TextureDefinition(
                value.TextureOffset, value.Width, value.Height, value.Type,
                value.PaletteId, value.MipmapPaletteId))
            .ToList();
        if (terrainDefinitions.Count + ordered.Length > MaxTerrainTextures)
            throw new InvalidDataException($"UYA terrain texture table exceeds {MaxTerrainTextures} entries.");

        var inventory = TextureInventoryBuilder.Build(
            BuildInventoryInputs([], ordered, new HashSet<int>()), cancellationToken);
        var optimization = PaletteOptimizer.Optimize(inventory, cancellationToken);
        if (optimization.Violations.Count > 0)
            throw new InvalidDataException(string.Join(' ', optimization.Violations.Select(value => value.Message)));
        var assignmentByKey = optimization.Assignments.ToDictionary(value => value.TextureKey, StringComparer.Ordinal);
        var paletteByIndex = optimization.Palettes.ToDictionary(value => value.PaletteIndex);
        var inventoryByIdentity = inventory.Textures.ToDictionary(value =>
            (value.Family, value.ClassId, value.Role, value.TextureIndex, value.AssetId));

        var mipmaps = LevelAssetReader.ReadMipmapDefinitions(
            headerBytes, sourceHeader.GsRamOffset,
            checked(sourceHeader.GsRamCount + sourceHeader.ExtraMipmapCount));
        var primaryMipmaps = mipmaps.Take(sourceHeader.GsRamCount).ToList();
        IReadOnlyList<LevelAssetMipmapDefinition> extraMipmaps =
            mipmaps.Skip(sourceHeader.GsRamCount).ToArray();
        var primaryPaletteLength = UyaStaticAssetLayout.FindRetainedPaletteLength(
            primaryMipmaps, sourceHeader, paletteBytes.Length);
        using var paletteOutput = Seed(paletteBytes[..primaryPaletteLength]);
        var occupiedPaletteRanges = primaryMipmaps
            .Select(value => (value.Offset1, End: checked(value.Offset1 + PaletteBlockLength(value))))
            .ToList();
        var paletteIds = new Dictionary<int, short>();
        foreach (var palette in optimization.Palettes.OrderBy(value => value.PaletteIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (palette.Encoding != PifTextureEncoding.Indexed8 || palette.Capacity != 256)
                throw new InvalidDataException("UYA terrain textures require 256-entry indexed8 palettes.");
            var bytes = new byte[0x400];
            foreach (var entry in palette.Entries)
            {
                var offset = entry.PaletteIndex * 4;
                bytes[offset] = entry.Color.Red;
                bytes[offset + 1] = entry.Color.Green;
                bytes[offset + 2] = entry.Color.Blue;
                bytes[offset + 3] = entry.Color.Alpha;
            }
            var paletteOffset = WritePrimaryPaletteBlock(
                paletteOutput, bytes, occupiedPaletteRanges);
            paletteIds.Add(palette.PaletteIndex, ToPaletteId(paletteOffset));
            primaryMipmaps.Add(new(-1, 0, 0, 0, paletteOffset, paletteOffset));
        }

        var sequenceTable = UyaStaticAssetLayout.ReadSequenceTable(
            headerBytes, sourceHeader, assetWadBytes.Length);
        var sourceSceneViewSize = sourceHeader.SceneViewSize;
        if (sourceSceneViewSize <= 0 || sourceSceneViewSize > assetWadBytes.Length)
            throw new InvalidDataException("UYA scene-view size exceeds the asset WAD bounds.");
        var modelDataOffset = UyaStaticAssetLayout.FindModelDataOffset(
            headerBytes, sourceHeader, sourceSceneViewSize);
        var preservedDataOffset = UyaStaticAssetLayout.FindPreservedDataOffset(
            sourceHeader, modelDataOffset);
        var textureDataOffset = sourceHeader.TextureDataOffset;
        var retainedTextureLength = textureDataOffset <= 0
            ? 0
            : UyaStaticAssetLayout.FindRetainedTextureLength(
                headerBytes, sourceHeader, preservedDataOffset, includeStaticTextures: true);
        var insertionOffset = textureDataOffset <= 0
            ? preservedDataOffset
            : checked(textureDataOffset + retainedTextureLength);
        if (insertionOffset <= 0 || insertionOffset > preservedDataOffset
            || insertionOffset > sourceSceneViewSize)
            throw new InvalidDataException(
                "UYA terrain textures have no safe insertion point before preserved asset data.");
        if (insertionOffset % AssetAlignment != 0)
            throw new InvalidDataException("UYA terrain texture insertion point is not aligned.");
        using var assetOutput = Seed(assetWadBytes[..insertionOffset]);
        var materialPixelOffsets = retainedTextureLength <= 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : ReadRetainedPixelOffsets(headerBytes, sourceHeader, assetWadBytes, retainedTextureLength);
        var allocations = AppendTerrainTextureData(
            ordered, inventoryByIdentity, assignmentByKey, paletteByIndex, paletteIds,
            assetOutput, ref textureDataOffset, materialPixelOffsets, paletteOutput, primaryMipmaps,
            occupiedPaletteRanges, terrainDefinitions, cancellationToken);
        Align(assetOutput, AssetAlignment);
        extraMipmaps = AppendExtraMipmaps(paletteOutput, paletteBytes, extraMipmaps);
        Align(paletteOutput, PaletteAlignment);
        var insertedLength = checked((int)assetOutput.Position - insertionOffset);
        assetOutput.Write(assetWadBytes[insertionOffset..sourceSceneViewSize]);
        var sceneViewSize = checked(sourceSceneViewSize + insertedLength);
        if (sequenceTable is not null) assetOutput.Write(assetWadBytes[sourceSceneViewSize..]);

        var sequenceTableWasLast = sequenceTable is not null
            && sourceHeader.LightCuboidsOffset == headerBytes.Length - UyaStaticAssetLayout.SequenceTableSize;
        using var headerOutput = Seed(sequenceTableWasLast
            ? headerBytes[..sourceHeader.LightCuboidsOffset]
            : headerBytes);
        var gsRamOffset = AppendMipmapTable(headerOutput, primaryMipmaps, extraMipmaps);
        var terrainTextureOffset = AppendTextureTable(headerOutput, terrainDefinitions);
        Align(headerOutput, AssetAlignment);
        var sequenceTableOffset = sequenceTable is null ? sourceHeader.LightCuboidsOffset
            : UyaStaticAssetLayout.AppendSequenceTable(
                headerOutput, sequenceTable, sceneViewSize - sourceSceneViewSize);

        var outputHeader = headerOutput.ToArray();
        RelocateAssetOffsets(
            outputHeader, headerBytes, sourceHeader,
            insertionOffset, sourceSceneViewSize, insertedLength);
        WriteInt32(outputHeader, 0x00, primaryMipmaps.Count);
        WriteInt32(outputHeader, 0x04, gsRamOffset);
        WriteInt32(outputHeader, 0x30, terrainDefinitions.Count);
        WriteInt32(outputHeader, 0x34, terrainTextureOffset);
        WriteInt32(outputHeader, 0x60, textureDataOffset);
        WriteInt32(outputHeader, 0x78, sequenceTableOffset);
        WriteInt32(outputHeader, 0x7c, sceneViewSize);
        WriteInt32(outputHeader, 0x84, extraMipmaps.Count);
        WriteInt32(outputHeader, 0x88, 0);
        WriteInt32(outputHeader, 0x8c, checked((int)assetOutput.Length));

        var result = new StaticAssetComposition(
            outputHeader, assetOutput.ToArray(), paletteOutput.ToArray(), inventory, optimization)
        {
            TerrainTextures = allocations,
        };
        ValidateTerrainAppend(result, sourceHeader, ordered);
        return result;
    }

    private static void RelocateAssetOffsets(
        byte[] outputHeader,
        ReadOnlySpan<byte> sourceHeaderBytes,
        LevelAssetHeader sourceHeader,
        int insertionOffset,
        int sourceSceneViewSize,
        int delta)
    {
        if (delta <= 0) return;
        ReadOnlySpan<(int HeaderOffset, int AssetOffset)> direct =
        [
            (0x08, sourceHeader.TerrainOffset),
            (0x0c, sourceHeader.OcclusionOffset),
            (0x10, sourceHeader.SkyOffset),
            (0x14, sourceHeader.CollisionOffset),
            (0x64, sourceHeader.ParticleTextureDataOffset),
            (0x68, sourceHeader.FxTextureDataOffset),
            (0xa0, sourceHeader.Unused3),
            (0xa4, sourceHeader.HeightmapOffset),
            (0xa8, sourceHeader.OcclusionOctreeOffset),
            (0xb0, sourceHeader.OcclusionRadiusOffset),
            (0xb8, sourceHeader.OcclusionRadius2Offset),
        ];
        foreach (var (headerOffset, assetOffset) in direct)
            if (ShouldRelocate(assetOffset, insertionOffset, sourceSceneViewSize))
                WriteInt32(outputHeader, headerOffset, checked(assetOffset + delta));

        RelocateModels(
            outputHeader,
            sourceHeader.MobyModelOffset,
            0x20,
            LevelAssetReader.ReadModelDefinitions(
                sourceHeaderBytes, sourceHeader.MobyModelOffset, sourceHeader.MobyModelCount)
                .Select(value => value.ModelOffset),
            insertionOffset,
            sourceSceneViewSize,
            delta);
        RelocateModels(
            outputHeader,
            sourceHeader.TieModelOffset,
            0x20,
            LevelAssetReader.ReadModelDefinitions(
                sourceHeaderBytes, sourceHeader.TieModelOffset, sourceHeader.TieModelCount)
                .Select(value => value.ModelOffset),
            insertionOffset,
            sourceSceneViewSize,
            delta);
        RelocateModels(
            outputHeader,
            sourceHeader.ShrubModelOffset,
            0x30,
            LevelAssetReader.ReadShrubDefinitions(
                sourceHeaderBytes, sourceHeader.ShrubModelOffset, sourceHeader.ShrubModelCount)
                .Select(value => value.ModelOffset),
            insertionOffset,
            sourceSceneViewSize,
            delta);
    }

    private static void RelocateModels(
        byte[] outputHeader,
        int tableOffset,
        int stride,
        IEnumerable<int> modelOffsets,
        int insertionOffset,
        int sourceSceneViewSize,
        int delta)
    {
        var index = 0;
        foreach (var modelOffset in modelOffsets)
        {
            if (ShouldRelocate(modelOffset, insertionOffset, sourceSceneViewSize))
                WriteInt32(
                    outputHeader,
                    checked(tableOffset + index * stride),
                    checked(modelOffset + delta));
            index++;
        }
    }

    private static bool ShouldRelocate(
        int offset,
        int insertionOffset,
        int sourceSceneViewSize) =>
        offset >= insertionOffset && offset < sourceSceneViewSize;

    private static IReadOnlyList<TerrainTextureAllocation> AppendTerrainTextureData(
        IReadOnlyList<TerrainTextureInput> textures,
        IReadOnlyDictionary<(TextureAssetFamily Family, int ClassId, TextureRole Role, int TextureIndex,
            string AssetId), TextureInventoryEntry> inventoryByIdentity,
        IReadOnlyDictionary<string, TexturePaletteAssignment> assignmentByKey,
        IReadOnlyDictionary<int, OptimizedPalette> paletteByIndex,
        IReadOnlyDictionary<int, short> paletteIds,
        MemoryStream assetOutput,
        ref int textureDataOffset,
        IDictionary<string, int> materialPixelOffsets,
        MemoryStream paletteOutput,
        ICollection<LevelAssetMipmapDefinition> primaryMipmaps,
        List<(int Start, int End)> occupiedPaletteRanges,
        ICollection<TextureDefinition> terrainDefinitions,
        CancellationToken cancellationToken)
    {
        var allocations = new List<TerrainTextureAllocation>(textures.Count);
        for (var index = 0; index < textures.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texture = textures[index];
            var inventoryEntry = inventoryByIdentity[(
                TextureAssetFamily.Terrain, 0, TextureRole.Material, index, texture.AssetId)];
            var assignment = assignmentByKey[inventoryEntry.Key];
            var rewritten = PifReader.Read(PaletteTextureWriter.RewritePif(
                texture.PifBytes.Span, assignment, paletteByIndex[assignment.PaletteIndex]));
            if (rewritten.IsSwizzled)
                throw new InvalidDataException($"UYA {inventoryEntry.Key} is unexpectedly swizzled.");
            if (textureDataOffset <= 0)
            {
                textureDataOffset = checked((int)assetOutput.Position);
                if (textureDataOffset <= 0)
                    throw new InvalidDataException("UYA texture data cannot start at offset zero.");
            }
            var pixelKey = HashMaterialPixels(rewritten);
            if (!materialPixelOffsets.TryGetValue(pixelKey, out var relativeOffset))
            {
                relativeOffset = checked((int)assetOutput.Position - textureDataOffset);
                assetOutput.Write(rewritten.PixelData);
                if (rewritten.MipPixelData.Count > 0) assetOutput.Write(rewritten.MipPixelData[0]);
                materialPixelOffsets.Add(pixelKey, relativeOffset);
            }
            var mipPaletteId = (short)-1;
            if (rewritten.MipPixelData.Count > 1)
            {
                var mipOffset = WritePrimaryPaletteBlock(
                    paletteOutput, rewritten.MipPixelData[1], occupiedPaletteRanges);
                mipPaletteId = ToPaletteId(mipOffset);
                primaryMipmaps.Add(new(
                    -1,
                    0x13,
                    checked((short)Math.Max(1, rewritten.Header.USize / 4)),
                    checked((short)Math.Max(1, rewritten.Header.VSize / 4)),
                    mipOffset,
                    mipOffset));
            }
            if (rewritten.MipPixelData.Count > 2)
                throw new InvalidDataException("UYA terrain textures support at most two mip levels.");
            var textureId = terrainDefinitions.Count;
            terrainDefinitions.Add(new(
                relativeOffset,
                checked((short)rewritten.Header.USize),
                checked((short)rewritten.Header.VSize),
                rewritten.MipPixelData.Count > 0 ? (short)3 : (short)1,
                paletteIds[assignment.PaletteIndex],
                mipPaletteId));
            allocations.Add(new(texture.AssetId, textureId));
        }
        return allocations;
    }

    private static void ValidateTerrainAppend(
        StaticAssetComposition result,
        LevelAssetHeader sourceHeader,
        IReadOnlyList<TerrainTextureInput> textures)
    {
        var header = LevelAssetReader.ReadHeader(result.HeaderBytes);
        var primaryMipmaps = LevelAssetReader.ReadMipmapDefinitions(
            result.HeaderBytes, header.GsRamOffset, header.GsRamCount);
        var appendedDefinitions = LevelAssetReader.ReadTextureDefinitions(
                result.HeaderBytes, header.TerrainTextureOffset, header.TerrainTextureCount)
            .Skip(sourceHeader.TerrainTextureCount)
            .ToArray();
        if (header.MobyModelCount != sourceHeader.MobyModelCount
            || header.TieModelCount != sourceHeader.TieModelCount
            || header.ShrubModelCount != sourceHeader.ShrubModelCount
            || header.MobyTextureCount != sourceHeader.MobyTextureCount
            || header.TieTextureCount != sourceHeader.TieTextureCount
            || header.ShrubTextureCount != sourceHeader.ShrubTextureCount
            || header.TerrainTextureCount != sourceHeader.TerrainTextureCount + textures.Count
            || result.TerrainTextures.Count != textures.Count
            || result.TerrainTextures.Select(value => value.AssetId)
                .SequenceEqual(textures.Select(value => value.AssetId), StringComparer.Ordinal) == false
            || appendedDefinitions.Any(definition =>
                !HasPrimaryGsRecord(primaryMipmaps, result.PaletteBytes.Length, 0, definition.PaletteId)
                || definition.MipmapPaletteId >= 0
                && !HasPrimaryGsRecord(
                    primaryMipmaps, result.PaletteBytes.Length, 0x13, definition.MipmapPaletteId))
            || header.SceneViewSize > result.AssetWadBytes.Length
            || header.DecompressedSize != result.AssetWadBytes.Length)
            throw new InvalidDataException("Appended UYA terrain textures failed semantic verification.");
    }

    private static bool HasPrimaryGsRecord(
        IReadOnlyList<LevelAssetMipmapDefinition> records,
        int paletteLength,
        int format,
        short destinationId) => destinationId >= 0 && records.Any(value =>
            value.TextureFormat == format
            && value.Offset2 == destinationId * PaletteAlignment
            && value.Offset1 == value.Offset2
            && value.Offset1 >= 0
            && value.Offset1 <= paletteLength - PaletteBlockLength(value));
}
