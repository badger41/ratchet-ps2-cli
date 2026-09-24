using System.Security.Cryptography;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Core.Textures.Palettes;

public enum TextureAssetFamily
{
    Moby = 1,
    Tie = 2,
    Shrub = 3,
}

public enum TextureRole
{
    Material = 0,
    Billboard = 1,
}

public sealed record TextureInventoryInput(
    string AssetId,
    TextureAssetFamily Family,
    int ClassId,
    int TextureIndex,
    TextureRole Role,
    ReadOnlyMemory<byte> PifBytes,
    IReadOnlyList<int>? MaterialSlots = null,
    IReadOnlyList<int>? ReservedPaletteIndexes = null);

public sealed record TextureColor(byte Red, byte Green, byte Blue, byte Alpha);

public sealed record TexturePaletteConstraint(
    PifTextureEncoding Encoding,
    int PaletteEntryCount,
    int PaletteFormat,
    int PaletteOrder,
    int MaxPaletteEntries);

public sealed record TexturePaletteEntry(
    int PaletteIndex,
    TextureColor Color,
    bool Referenced,
    bool Reserved);

public sealed record TextureIndexUsage(
    int SourcePixelIndex,
    int PaletteIndex,
    TextureColor Color,
    long Frequency,
    IReadOnlyList<int> MipFrequencies);

public sealed record TextureInventoryEntry(
    string Key,
    string AssetId,
    TextureAssetFamily Family,
    int ClassId,
    int TextureIndex,
    TextureRole Role,
    int Width,
    int Height,
    int MipLevelCount,
    long TexelCount,
    string SourceSha256,
    TexturePaletteConstraint Constraint,
    IReadOnlyList<int> MaterialSlots,
    IReadOnlyList<TexturePaletteEntry> PaletteEntries,
    IReadOnlyList<TextureIndexUsage> IndexUsages);

public sealed record TextureInventory(
    int SchemaVersion,
    IReadOnlyList<TextureInventoryEntry> Textures,
    long TexelCount);

public static class TextureInventoryBuilder
{
    public const int SchemaVersion = 1;
    private const int MaxDimension = 4_096;
    private const int MaxMipLevels = 16;
    private const int MaxPifBytes = 64 * 1024 * 1024;

    public static TextureInventory Build(
        IEnumerable<TextureInventoryInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ordered = inputs.Select(ValidateInput)
            .OrderBy(value => value.Family)
            .ThenBy(value => value.ClassId)
            .ThenBy(value => value.Role)
            .ThenBy(value => value.TextureIndex)
            .ThenBy(value => value.AssetId, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered.GroupBy(Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate texture inventory input {duplicate.Key}.", nameof(inputs));

        var textures = new TextureInventoryEntry[ordered.Length];
        long texelCount = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            textures[index] = BuildTexture(ordered[index], cancellationToken);
            texelCount = checked(texelCount + textures[index].TexelCount);
        }
        return new(SchemaVersion, textures, texelCount);
    }

    private static TextureInventoryInput ValidateInput(TextureInventoryInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.AssetId) || input.AssetId.Length > 256)
            throw new ArgumentException("Texture asset ID is missing or too long.", nameof(input));
        if (!Enum.IsDefined(input.Family) || !Enum.IsDefined(input.Role))
            throw new ArgumentException("Texture family or role is invalid.", nameof(input));
        if (input.ClassId < 0 || input.TextureIndex < 0)
            throw new ArgumentException("Texture class and texture indexes cannot be negative.", nameof(input));
        if (input.PifBytes.IsEmpty || input.PifBytes.Length > MaxPifBytes)
            throw new ArgumentException("Texture PIF size is invalid.", nameof(input));
        if (input.MaterialSlots?.Any(value => value < 0) == true
            || input.ReservedPaletteIndexes?.Any(value => value < 0) == true)
            throw new ArgumentException("Texture material and reserved indexes cannot be negative.", nameof(input));
        return input;
    }

    private static TextureInventoryEntry BuildTexture(
        TextureInventoryInput input,
        CancellationToken cancellationToken)
    {
        PifTextureData texture;
        try
        {
            using var headerStream = new MemoryStream(input.PifBytes.ToArray(), writable: false);
            var header = PifReader.ReadHeader(headerStream);
            if (!header.HasValidMagic
                || header.USize is <= 0 or > MaxDimension
                || header.VSize is <= 0 or > MaxDimension
                || header.MipLevels is <= 0 or > MaxMipLevels
                || checked((long)header.USize * header.VSize) > MaxDimension * (long)MaxDimension)
                throw new InvalidDataException("PIF header dimensions, mip count, or magic are invalid.");
            texture = PifReader.Read(input.PifBytes.Span);
            if (header.FileSize != input.PifBytes.Length
                || PifWriter.GetSerializedSize(texture) != input.PifBytes.Length)
                throw new InvalidDataException("PIF declared and actual allocation sizes differ.");
        }
        catch (Exception exception) when (exception is InvalidDataException
            or ArgumentException or NotSupportedException or OverflowException or EndOfStreamException)
        {
            throw new InvalidDataException($"Texture {Key(input)} is invalid: {exception.Message}", exception);
        }

        var paletteCount = texture.PaletteData.Length / 4;
        var maxPaletteEntries = texture.Encoding == PifTextureEncoding.Indexed4 ? 16 : 256;
        if (paletteCount is <= 0 or > 256 || paletteCount > maxPaletteEntries)
            throw new InvalidDataException($"Texture {Key(input)} palette size is invalid for {texture.Encoding}.");
        var reserved = (input.ReservedPaletteIndexes ?? []).Distinct().Order().ToArray();
        if (reserved.Any(value => value >= paletteCount))
            throw new InvalidDataException($"Texture {Key(input)} has a reserved index outside its palette.");

        var levels = new[] { texture.PixelData }.Concat(texture.MipPixelData).ToArray();
        var frequencies = new int[maxPaletteEntries, levels.Length];
        long texelCount = 0;
        for (var level = 0; level < levels.Length; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = Math.Max(1, texture.Header.USize >> level);
            var height = Math.Max(1, texture.Header.VSize >> level);
            var count = checked(width * height);
            CountIndices(texture.Encoding, levels[level], count, frequencies, level, Key(input), cancellationToken);
            texelCount = checked(texelCount + count);
        }

        var usages = new List<TextureIndexUsage>();
        var referencedPaletteIndexes = new HashSet<int>();
        for (var sourceIndex = 0; sourceIndex < maxPaletteEntries; sourceIndex++)
        {
            var mipFrequencies = Enumerable.Range(0, levels.Length)
                .Select(level => frequencies[sourceIndex, level]).ToArray();
            var frequency = mipFrequencies.Sum(value => (long)value);
            if (frequency == 0) continue;
            var paletteIndex = ResolvePaletteIndex(texture.Encoding, sourceIndex);
            if (paletteIndex >= paletteCount)
                throw new InvalidDataException(
                    $"Texture {Key(input)} pixel index {sourceIndex} resolves outside its palette.");
            referencedPaletteIndexes.Add(paletteIndex);
            usages.Add(new(sourceIndex, paletteIndex, ReadColor(texture.PaletteData, paletteIndex), frequency, mipFrequencies));
        }

        var reservedSet = reserved.ToHashSet();
        var palette = Enumerable.Range(0, paletteCount).Select(index => new TexturePaletteEntry(
            index,
            ReadColor(texture.PaletteData, index),
            referencedPaletteIndexes.Contains(index),
            reservedSet.Contains(index))).ToArray();
        var materialSlots = (input.MaterialSlots ?? [input.TextureIndex]).Distinct().Order().ToArray();
        return new(
            Key(input), input.AssetId, input.Family, input.ClassId, input.TextureIndex, input.Role,
            texture.Header.USize, texture.Header.VSize, levels.Length, texelCount,
            Convert.ToHexString(SHA256.HashData(input.PifBytes.Span)).ToLowerInvariant(),
            new(texture.Encoding, paletteCount, texture.Header.PaletteFormat,
                texture.Header.PaletteOrder, maxPaletteEntries),
            materialSlots, palette, usages);
    }

    private static void CountIndices(
        PifTextureEncoding encoding,
        ReadOnlySpan<byte> pixels,
        int texelCount,
        int[,] frequencies,
        int level,
        string key,
        CancellationToken cancellationToken)
    {
        var expectedBytes = encoding == PifTextureEncoding.Indexed4 ? (texelCount + 1) / 2 : texelCount;
        if (pixels.Length != expectedBytes)
            throw new InvalidDataException($"Texture {key} mip {level} pixel allocation is invalid.");
        if (encoding == PifTextureEncoding.Indexed8)
        {
            for (var texel = 0; texel < pixels.Length; texel++)
            {
                if ((texel & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                frequencies[pixels[texel], level]++;
            }
            return;
        }
        if (encoding != PifTextureEncoding.Indexed4)
            throw new InvalidDataException($"Texture {key} encoding is unsupported.");
        for (var texel = 0; texel < texelCount; texel++)
        {
            if ((texel & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var packed = pixels[texel / 2];
            frequencies[texel % 2 == 0 ? packed & 0x0f : packed >> 4, level]++;
        }
    }

    private static int ResolvePaletteIndex(PifTextureEncoding encoding, int sourceIndex) =>
        encoding == PifTextureEncoding.Indexed8
            ? TextureConverter.DecodePaletteIndex((byte)sourceIndex)
            : sourceIndex;

    private static TextureColor ReadColor(byte[] palette, int index)
    {
        var offset = checked(index * 4);
        return new(palette[offset], palette[offset + 1], palette[offset + 2], palette[offset + 3]);
    }

    private static string Key(TextureInventoryInput input) =>
        $"{input.Family.ToString().ToLowerInvariant()}:{input.ClassId:X4}:{input.Role.ToString().ToLowerInvariant()}:{input.TextureIndex:D4}:{input.AssetId}";
}
