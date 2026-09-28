using System.Buffers.Binary;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaStaticAssetLayout
{
    private const int HeaderFixedSize = 0xc0;
    private const int AssetAlignment = 0x10;
    private const int PaletteAlignment = 0x100;
    internal const int SequenceTableSize = 0x400;

    public static (IReadOnlyList<DlAssetMipmapDefinition> Primary,
        IReadOnlyList<DlAssetMipmapDefinition> Extra) ReadRetainedMipmaps(
        ReadOnlySpan<byte> headerBytes,
        DlAssetHeader header)
    {
        var offsets = DlAssetReader.ReadTextureDefinitions(
                headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount)
            .Concat(DlAssetReader.ReadTextureDefinitions(
                    headerBytes, header.MobyTextureOffset, header.MobyTextureCount)
                .Where(texture => texture.Type == 0))
            .SelectMany(texture => new[] { texture.PaletteId, texture.MipmapPaletteId })
            .Where(value => value >= 0)
            .Select(value => value * PaletteAlignment)
            .Append(header.ChromePaletteOffset)
            .Append(header.GlassPaletteOffset)
            .ToHashSet();
        var mipmaps = DlAssetReader.ReadMipmapDefinitions(
            headerBytes, header.GsRamOffset, checked(header.GsRamCount + header.ExtraMipmapCount));
        return (
            mipmaps.Take(header.GsRamCount).Where(value => offsets.Contains(value.Offset1)).ToArray(),
            mipmaps.Skip(header.GsRamCount).ToArray());
    }

    public static int FindRetainedPaletteLength(
        IReadOnlyList<DlAssetMipmapDefinition> mipmaps,
        DlAssetHeader header,
        int paletteLength)
    {
        var length = mipmaps.Select(value => checked(value.Offset1 + (value.TextureFormat switch
            {
                0 => 0x400,
                1 => 0x200,
                0x13 => checked(value.Width * value.Height),
                _ => throw new InvalidDataException(
                    $"Unsupported UYA GS RAM texture format 0x{value.TextureFormat:X}.")
            })))
            .Append(header.ChromePaletteOffset > 0 ? checked(header.ChromePaletteOffset + 0x400) : 0)
            .Append(header.GlassPaletteOffset > 0 ? checked(header.GlassPaletteOffset + 0x400) : 0)
            .Max();
        length = checked((length + PaletteAlignment - 1) / PaletteAlignment * PaletteAlignment);
        if (length > paletteLength)
            throw new InvalidDataException("UYA retained GS RAM data exceeds the palette WAD bounds.");
        return length;
    }

    public static MemoryStream CreateAssetOutput(
        ReadOnlySpan<byte> bytes,
        int textureDataOffset,
        int retainedTextureLength)
    {
        if (textureDataOffset <= 0) return Seed(bytes);
        var retainedEnd = checked(textureDataOffset + retainedTextureLength);
        if (retainedEnd > bytes.Length)
            throw new InvalidDataException("UYA texture data offset exceeds the asset WAD bounds.");
        return Seed(bytes[..retainedEnd]);
    }

    public static int FindRetainedTextureLength(
        ReadOnlySpan<byte> headerBytes,
        DlAssetHeader header,
        int preservedDataOffset)
    {
        if (header.TextureDataOffset <= 0) return 0;
        var maxEnd = 0;
        foreach (var texture in DlAssetReader.ReadTextureDefinitions(
                     headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount))
        {
            if ((texture.Type & 1) == 0) continue;
            if (texture.TextureOffset < 0 || texture.Width <= 0 || texture.Height <= 0)
                throw new InvalidDataException("UYA terrain texture definition has invalid bounds.");
            var end = checked(texture.TextureOffset + texture.Width * texture.Height);
            if ((texture.Type & 2) != 0)
                end = checked(end + Math.Max(1, texture.Width / 2) * Math.Max(1, texture.Height / 2));
            maxEnd = Math.Max(maxEnd, end);
        }
        var retainedLength = checked((maxEnd + AssetAlignment - 1) / AssetAlignment * AssetAlignment);
        if (checked(header.TextureDataOffset + retainedLength) > preservedDataOffset)
            throw new InvalidDataException("UYA terrain texture data overlaps the preserved asset region.");
        return retainedLength;
    }

    public static int FindModelDataOffset(
        ReadOnlySpan<byte> headerBytes,
        DlAssetHeader header,
        int assetWadLength)
    {
        var offsets = DlAssetReader.ReadModelDefinitions(
                headerBytes, header.MobyModelOffset, header.MobyModelCount)
            .Select(value => value.ModelOffset)
            .Concat(DlAssetReader.ReadModelDefinitions(
                headerBytes, header.TieModelOffset, header.TieModelCount).Select(value => value.ModelOffset))
            .Concat(DlAssetReader.ReadShrubDefinitions(
                headerBytes, header.ShrubModelOffset, header.ShrubModelCount).Select(value => value.ModelOffset))
            .Where(value => value > 0)
            .ToArray();
        var offset = offsets.Length > 0 ? offsets.Min() : assetWadLength;
        if (offset < 0 || offset > assetWadLength)
            throw new InvalidDataException("UYA model data offset exceeds the asset WAD bounds.");
        return offset;
    }

    public static int FindPreservedDataOffset(DlAssetHeader header, int modelDataOffset)
    {
        var offset = new[] { header.ParticleTextureDataOffset, header.FxTextureDataOffset, modelDataOffset }
            .Where(value => value > header.TextureDataOffset)
            .DefaultIfEmpty(modelDataOffset)
            .Min();
        if (offset > modelDataOffset)
            throw new InvalidDataException("UYA particle and FX texture data overlap the model data region.");
        return offset;
    }

    public static int RelocatePreservedOffset(int offset, int sourceStart, int sourceEnd, int outputStart) =>
        offset >= sourceStart && offset < sourceEnd
            ? checked(outputStart + offset - sourceStart)
            : offset;

    public static byte[]? ReadSequenceTable(
        ReadOnlySpan<byte> headerBytes,
        DlAssetHeader header,
        int assetWadLength)
    {
        if (header.SceneViewSize <= 0 || header.SceneViewSize == assetWadLength) return null;
        if (header.SceneViewSize > assetWadLength
            || header.LightCuboidsOffset < HeaderFixedSize
            || header.LightCuboidsOffset > headerBytes.Length - SequenceTableSize)
            throw new InvalidDataException("UYA trailing sequence data bounds are invalid.");
        var table = headerBytes.Slice(header.LightCuboidsOffset, SequenceTableSize).ToArray();
        for (var offset = 0; offset < table.Length; offset += sizeof(int))
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset));
            if (value != 0 && (value < header.SceneViewSize
                    || value >= assetWadLength || value % AssetAlignment != 0))
                throw new InvalidDataException("UYA trailing sequence table contains an invalid asset offset.");
        }
        return table;
    }

    public static int AppendSequenceTable(MemoryStream output, byte[] table, int delta)
    {
        Align(output, AssetAlignment);
        var tableOffset = checked((int)output.Position);
        for (var offset = 0; offset < table.Length; offset += sizeof(int))
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset));
            if (value != 0)
                BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(offset), checked(value + delta));
        }
        output.Write(table);
        return tableOffset;
    }

    private static MemoryStream Seed(ReadOnlySpan<byte> bytes)
    {
        var output = new MemoryStream(bytes.Length + 4096);
        output.Write(bytes);
        return output;
    }

    private static void Align(Stream stream, int alignment)
    {
        var padding = (alignment - stream.Position % alignment) % alignment;
        if (padding > 0) stream.Write(new byte[padding]);
    }
}
