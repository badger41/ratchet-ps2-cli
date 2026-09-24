using System.Buffers.Binary;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaStaticAssetComposer
{
    private const int HeaderFixedSize = 0xc0;
    private const int AssetAlignment = 0x10;
    private const int PaletteAlignment = 0x100;
    private const int MaxFamilyTextures = byte.MaxValue;

    public static StaticAssetComposition Compose(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        ReadOnlySpan<byte> paletteBytes,
        IReadOnlyList<StaticAssetInput> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (headerBytes.Length < HeaderFixedSize)
            throw new InvalidDataException("UYA asset header is shorter than its fixed header.");
        var ordered = assets.Select(ValidateAsset)
            .OrderBy(value => value.Family)
            .ThenBy(value => value.ClassId)
            .ThenBy(value => value.AssetId, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered.GroupBy(value => (value.Family, value.ClassId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Multiple {duplicate.Key.Family} assets target class 0x{duplicate.Key.ClassId:X4}.", nameof(assets));

        var inventory = TextureInventoryBuilder.Build(BuildInventoryInputs(ordered), cancellationToken);
        var optimization = PaletteOptimizer.Optimize(inventory, cancellationToken);
        if (optimization.Violations.Count > 0)
            throw new InvalidDataException(string.Join(' ', optimization.Violations.Select(value => value.Message)));
        var assignmentByKey = optimization.Assignments.ToDictionary(value => value.TextureKey, StringComparer.Ordinal);
        var paletteByIndex = optimization.Palettes.ToDictionary(value => value.PaletteIndex);
        var inventoryByIdentity = inventory.Textures.ToDictionary(value =>
            (value.Family, value.ClassId, value.Role, value.TextureIndex, value.AssetId));

        using var paletteOutput = Seed(paletteBytes);
        var mipmaps = ReadMipmaps(headerBytes);
        var primaryMipmaps = mipmaps.Primary.ToList();
        var paletteIds = new Dictionary<int, short>();
        foreach (var palette in optimization.Palettes.OrderBy(value => value.PaletteIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (palette.Encoding != PifTextureEncoding.Indexed8 || palette.Capacity != 256)
                throw new InvalidDataException("UYA static assets require 256-entry indexed8 palettes.");
            var bytes = new byte[0x400];
            foreach (var entry in palette.Entries)
            {
                var colorOffset = entry.PaletteIndex * 4;
                bytes[colorOffset] = entry.Color.Red;
                bytes[colorOffset + 1] = entry.Color.Green;
                bytes[colorOffset + 2] = entry.Color.Blue;
                bytes[colorOffset + 3] = entry.Color.Alpha;
            }
            var paletteOffset = AppendPaletteBlock(paletteOutput, bytes);
            paletteIds.Add(palette.PaletteIndex, ToPaletteId(paletteOffset));
            primaryMipmaps.Add(new(-1, 0, 0, 0, paletteOffset, paletteOffset));
        }

        using var assetOutput = Seed(assetWadBytes);
        Align(assetOutput, AssetAlignment);
        var sourceHeader = DlAssetReader.ReadHeader(headerBytes);
        var textureDataOffset = sourceHeader.TextureDataOffset;
        var definitions = ordered.ToDictionary(value => value, value => value.DefinitionBytes.ToArray());
        var textureDefinitions = Enum.GetValues<TextureAssetFamily>()
            .ToDictionary(value => value, _ => new List<TextureDefinition>());
        var sharedMaterialIds = Enum.GetValues<TextureAssetFamily>()
            .ToDictionary(value => value, _ => new Dictionary<(string SourceHash, int Palette), byte>());
        foreach (var asset in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var materialIds = new List<byte>();
            var materialIndex = 0;
            StaticAssetTexture? billboard = null;
            if (asset.Family == TextureAssetFamily.Shrub) definitions[asset].AsSpan(0x20, 0x10).Clear();
            foreach (var texture in asset.Textures)
            {
                var roleIndex = texture.Role == TextureRole.Material ? materialIndex++ : 0;
                var inventoryEntry = inventoryByIdentity[(asset.Family, asset.ClassId, texture.Role, roleIndex, asset.AssetId)];
                var assignment = assignmentByKey[inventoryEntry.Key];
                var optimizedPalette = paletteByIndex[assignment.PaletteIndex];
                var rewrittenBytes = PaletteTextureWriter.RewritePif(
                    texture.PifBytes.Span, assignment, optimizedPalette);
                var rewritten = PifReader.Read(rewrittenBytes);
                if (rewritten.IsSwizzled)
                    throw new InvalidDataException($"UYA {inventoryEntry.Key} is unexpectedly swizzled.");
                if (texture.Role == TextureRole.Billboard)
                {
                    billboard = texture;
                    WriteBillboard(definitions[asset], rewritten, paletteIds[assignment.PaletteIndex],
                        paletteOutput, primaryMipmaps);
                    continue;
                }

                var familyTextures = textureDefinitions[asset.Family];
                var sharedKey = (inventoryEntry.SourceSha256, assignment.PaletteIndex);
                if (sharedMaterialIds[asset.Family].TryGetValue(sharedKey, out var sharedId))
                {
                    materialIds.Add(sharedId);
                    continue;
                }
                if (familyTextures.Count >= MaxFamilyTextures)
                    throw new InvalidDataException($"UYA {asset.Family} texture table exceeds 255 addressable entries.");
                if (textureDataOffset <= 0)
                {
                    textureDataOffset = checked((int)assetOutput.Position);
                    if (textureDataOffset <= 0) throw new InvalidDataException("UYA texture data cannot start at offset zero.");
                }
                var relativeOffset = checked((int)assetOutput.Position - textureDataOffset);
                assetOutput.Write(rewritten.PixelData);
                if (rewritten.MipPixelData.Count > 0) assetOutput.Write(rewritten.MipPixelData[0]);
                var mipPaletteId = (short)-1;
                if (rewritten.MipPixelData.Count > 1)
                {
                    var mipOffset = AppendPaletteBlock(paletteOutput, rewritten.MipPixelData[1]);
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
                    throw new InvalidDataException("UYA material textures support at most two mip levels.");
                familyTextures.Add(new(
                    relativeOffset,
                    checked((short)rewritten.Header.USize),
                    checked((short)rewritten.Header.VSize),
                    rewritten.MipPixelData.Count > 0 ? (short)3 : (short)1,
                    paletteIds[assignment.PaletteIndex],
                    mipPaletteId));
                var materialId = checked((byte)(familyTextures.Count - 1));
                sharedMaterialIds[asset.Family].Add(sharedKey, materialId);
                materialIds.Add(materialId);
            }
            if (asset.Family != TextureAssetFamily.Shrub && billboard is not null)
                throw new InvalidDataException("Only UYA shrubs may carry billboard textures.");
            WriteMaterialIds(definitions[asset], materialIds);
        }

        var modelOffsets = new Dictionary<StaticAssetInput, int>();
        foreach (var asset in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Align(assetOutput, AssetAlignment);
            modelOffsets.Add(asset, checked((int)assetOutput.Position));
            assetOutput.Write(asset.ModelBytes.Span);
        }

        using var headerOutput = Seed(headerBytes);
        var gsRamOffset = AppendMipmapTable(headerOutput, primaryMipmaps, mipmaps.Extra);
        var tableLocations = new Dictionary<TextureAssetFamily, TableLocations>();
        foreach (var family in Enum.GetValues<TextureAssetFamily>())
        {
            var familyAssets = ordered.Where(value => value.Family == family).ToArray();
            var modelOffset = AppendModelTable(
                headerOutput, family, familyAssets, definitions, modelOffsets);
            var textureOffset = AppendTextureTable(headerOutput, textureDefinitions[family]);
            tableLocations.Add(family, new(modelOffset, textureOffset));
        }

        var outputHeader = headerOutput.ToArray();
        WriteInt32(outputHeader, 0x00, primaryMipmaps.Count);
        WriteInt32(outputHeader, 0x04, gsRamOffset);
        WriteInt32(outputHeader, 0x60, textureDataOffset);
        WriteInt32(outputHeader, 0x84, mipmaps.Extra.Count);
        foreach (var family in Enum.GetValues<TextureAssetFamily>())
        {
            var offsets = FamilyOffsets(family);
            var count = ordered.Count(value => value.Family == family);
            WriteInt32(outputHeader, offsets.ModelCount, count);
            WriteInt32(outputHeader, offsets.ModelOffset, count == 0 ? 0 : tableLocations[family].Models);
            WriteInt32(outputHeader, offsets.TextureCount, textureDefinitions[family].Count);
            WriteInt32(outputHeader, offsets.TextureOffset,
                textureDefinitions[family].Count == 0 ? 0 : tableLocations[family].Textures);
        }

        var result = new StaticAssetComposition(
            outputHeader, assetOutput.ToArray(), paletteOutput.ToArray(), inventory, optimization);
        ValidateComposition(result, ordered);
        return result;
    }

    private static StaticAssetInput ValidateAsset(StaticAssetInput? asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var definitionSize = asset.Family == TextureAssetFamily.Shrub ? 0x30 : 0x20;
        if (!Enum.IsDefined(asset.Family)
            || asset.ClassId is < 0 or > ushort.MaxValue
            || string.IsNullOrWhiteSpace(asset.AssetId)
            || asset.DefinitionBytes.Length != definitionSize
            || asset.ModelBytes.IsEmpty
            || asset.Textures is null
            || asset.Textures.Any(value => value is null || !Enum.IsDefined(value.Role) || value.PifBytes.IsEmpty)
            || asset.Textures.Count(value => value.Role == TextureRole.Material) > 16
            || asset.Textures.Count(value => value.Role == TextureRole.Billboard) > 1)
            throw new ArgumentException("UYA static asset input is invalid.", nameof(asset));
        return asset;
    }

    private static IReadOnlyList<TextureInventoryInput> BuildInventoryInputs(
        IReadOnlyList<StaticAssetInput> assets)
    {
        var result = new List<TextureInventoryInput>();
        foreach (var asset in assets)
        {
            var materialIndex = 0;
            var billboardIndex = 0;
            foreach (var texture in asset.Textures)
            {
                var index = texture.Role == TextureRole.Material ? materialIndex++ : billboardIndex++;
                result.Add(new(
                    asset.AssetId, asset.Family, asset.ClassId, index, texture.Role, texture.PifBytes,
                    texture.Role == TextureRole.Material ? [index] : []));
            }
        }
        return result;
    }

    private static void WriteBillboard(
        byte[] definition,
        PifTextureData texture,
        short paletteId,
        MemoryStream paletteOutput,
        ICollection<DlAssetMipmapDefinition> mipmaps)
    {
        if (texture.MipPixelData.Count > 3)
            throw new InvalidDataException("UYA shrub billboards support at most three mip levels.");
        var levels = new[] { texture.PixelData }.Concat(texture.MipPixelData).ToArray();
        var offsets = new short[levels.Length];
        var width = texture.Header.USize;
        var height = texture.Header.VSize;
        for (var index = 0; index < levels.Length; index++)
        {
            if (index > 0)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            var offset = AppendPaletteBlock(paletteOutput, levels[index]);
            offsets[index] = ToPaletteId(offset);
            mipmaps.Add(new(-1, 0x13, checked((short)width), checked((short)height), offset, offset));
        }
        WriteInt16(definition, 0x20, checked((short)texture.Header.USize));
        WriteInt16(definition, 0x22, checked((short)texture.Header.VSize));
        WriteInt16(definition, 0x24, checked((short)levels.Length));
        WriteInt16(definition, 0x26, paletteId);
        WriteInt16(definition, 0x28, offsets[0]);
        for (var index = 1; index < 4; index++)
            WriteInt16(definition, 0x28 + index * 2, index < offsets.Length ? offsets[index] : (short)0);
    }

    private static void WriteMaterialIds(byte[] definition, IReadOnlyList<byte> ids)
    {
        definition.AsSpan(0x10, 0x10).Fill(byte.MaxValue);
        ids.ToArray().CopyTo(definition, 0x10);
    }

    private static (IReadOnlyList<DlAssetMipmapDefinition> Primary, IReadOnlyList<DlAssetMipmapDefinition> Extra)
        ReadMipmaps(ReadOnlySpan<byte> headerBytes)
    {
        var header = DlAssetReader.ReadHeader(headerBytes);
        var values = DlAssetReader.ReadMipmapDefinitions(
            headerBytes, header.GsRamOffset, checked(header.GsRamCount + header.ExtraMipmapCount));
        return (values.Take(header.GsRamCount).ToArray(), values.Skip(header.GsRamCount).ToArray());
    }

    private static int AppendMipmapTable(
        MemoryStream output,
        IReadOnlyList<DlAssetMipmapDefinition> primary,
        IReadOnlyList<DlAssetMipmapDefinition> extra)
    {
        if (primary.Count + extra.Count == 0) return 0;
        Align(output, AssetAlignment);
        var offset = checked((int)output.Position);
        foreach (var mipmap in primary.Concat(extra))
        {
            WriteInt32(output, mipmap.TextureFormat);
            WriteInt16(output, mipmap.Width);
            WriteInt16(output, mipmap.Height);
            WriteInt32(output, mipmap.Offset1);
            WriteInt32(output, mipmap.Offset2);
        }
        return offset;
    }

    private static int AppendModelTable(
        MemoryStream output,
        TextureAssetFamily family,
        IReadOnlyList<StaticAssetInput> assets,
        IReadOnlyDictionary<StaticAssetInput, byte[]> definitions,
        IReadOnlyDictionary<StaticAssetInput, int> modelOffsets)
    {
        if (assets.Count == 0) return 0;
        Align(output, AssetAlignment);
        var offset = checked((int)output.Position);
        foreach (var asset in assets)
        {
            var definition = definitions[asset];
            WriteInt32(definition, 0x00, modelOffsets[asset]);
            WriteInt32(definition, 0x04, asset.ClassId);
            output.Write(definition);
        }
        return offset;
    }

    private static int AppendTextureTable(MemoryStream output, IReadOnlyList<TextureDefinition> textures)
    {
        if (textures.Count == 0) return 0;
        Align(output, AssetAlignment);
        var offset = checked((int)output.Position);
        foreach (var texture in textures)
        {
            WriteInt32(output, texture.Offset);
            WriteInt16(output, texture.Width);
            WriteInt16(output, texture.Height);
            WriteInt16(output, texture.Type);
            WriteInt16(output, texture.PaletteId);
            WriteInt16(output, texture.MipmapPaletteId);
            WriteInt16(output, -1);
        }
        return offset;
    }

    private static void ValidateComposition(StaticAssetComposition result, IReadOnlyList<StaticAssetInput> assets)
    {
        var header = DlAssetReader.ReadHeader(result.HeaderBytes);
        foreach (var family in Enum.GetValues<TextureAssetFamily>())
        {
            var offsets = FamilyOffsets(family);
            var expected = assets.Where(value => value.Family == family).ToArray();
            if (ReadInt32(result.HeaderBytes, offsets.ModelCount) != expected.Length)
                throw new InvalidDataException($"Composed UYA {family} model count failed verification.");
            var definitions = family == TextureAssetFamily.Shrub
                ? DlAssetReader.ReadShrubDefinitions(result.HeaderBytes,
                    ReadInt32(result.HeaderBytes, offsets.ModelOffset), expected.Length)
                    .Select(value => (value.ModelId, value.ModelOffset, value.TextureIds)).ToArray()
                : DlAssetReader.ReadModelDefinitions(result.HeaderBytes,
                    ReadInt32(result.HeaderBytes, offsets.ModelOffset), expected.Length)
                    .Select(value => (value.ModelId, value.ModelOffset, value.TextureIds)).ToArray();
            if (definitions.Length != expected.Length) throw new InvalidDataException("UYA model table count changed.");
            for (var index = 0; index < expected.Length; index++)
            {
                var definition = definitions[index];
                if (definition.ModelId != expected[index].ClassId
                    || definition.ModelOffset < 0
                    || definition.ModelOffset + expected[index].ModelBytes.Length > result.AssetWadBytes.Length
                    || !result.AssetWadBytes.AsSpan(definition.ModelOffset, expected[index].ModelBytes.Length)
                        .SequenceEqual(expected[index].ModelBytes.Span))
                    throw new InvalidDataException($"Composed UYA {family} class 0x{expected[index].ClassId:X4} failed verification.");
            }
        }
        if (header.GsRamCount < result.Optimization.Palettes.Count)
            throw new InvalidDataException("Composed UYA header is missing optimized palette records.");
    }

    private static (int ModelCount, int ModelOffset, int TextureCount, int TextureOffset) FamilyOffsets(
        TextureAssetFamily family) => family switch
    {
        TextureAssetFamily.Moby => (0x18, 0x1c, 0x38, 0x3c),
        TextureAssetFamily.Tie => (0x20, 0x24, 0x40, 0x44),
        TextureAssetFamily.Shrub => (0x28, 0x2c, 0x48, 0x4c),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static MemoryStream Seed(ReadOnlySpan<byte> bytes)
    {
        var output = new MemoryStream(bytes.Length + 4096);
        output.Write(bytes);
        return output;
    }

    private static int AppendPaletteBlock(MemoryStream output, ReadOnlySpan<byte> bytes)
    {
        Align(output, PaletteAlignment);
        var offset = checked((int)output.Position);
        output.Write(bytes);
        return offset;
    }

    private static short ToPaletteId(int offset)
    {
        if (offset % PaletteAlignment != 0 || offset / PaletteAlignment > short.MaxValue)
            throw new InvalidDataException("UYA palette offset exceeds its signed 16-bit block ID.");
        return (short)(offset / PaletteAlignment);
    }

    private static void Align(Stream stream, int alignment)
    {
        var padding = (alignment - stream.Position % alignment) % alignment;
        if (padding > 0) stream.Write(new byte[padding]);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteInt16(byte[] bytes, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, sizeof(short)), value);

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record TextureDefinition(
        int Offset,
        short Width,
        short Height,
        short Type,
        short PaletteId,
        short MipmapPaletteId);

    private readonly record struct TableLocations(int Models, int Textures);
}
