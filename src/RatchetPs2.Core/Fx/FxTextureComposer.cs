using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Fx;

public sealed record FxIndexedTexture(
    int Width,
    int Height,
    ReadOnlyMemory<byte> PaletteBytes,
    ReadOnlyMemory<byte> PixelBytes);

public sealed record FxTextureReplacement(int Index, FxIndexedTexture Texture);

public sealed record FxTextureCompositionOptions(
    int MaximumHeaderBytes = 4 * 1024 * 1024,
    int MaximumStoredAssetBytes = 256 * 1024 * 1024,
    int MaximumDecompressedAssetBytes = 256 * 1024 * 1024);

public sealed record FxTextureComposition(
    byte[] HeaderBytes,
    byte[] AssetBytes,
    bool IsBasePassThrough);

public static class FxTextureComposer
{
    private sealed record PlannedTexture(
        int Index,
        FxIndexedTexture Texture,
        int PaletteOffset,
        int PixelOffset);

    public static FxTextureComposition Compose(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> storedAssetBytes,
        IReadOnlyList<FxTextureReplacement> replacements,
        IReadOnlyList<FxIndexedTexture> additions,
        FxTextureCompositionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(additions);
        options ??= new();
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (headerBytes.Length > options.MaximumHeaderBytes)
            throw new InvalidDataException("FX asset header exceeds the configured size limit.");
        if (storedAssetBytes.Length > options.MaximumStoredAssetBytes)
            throw new InvalidDataException("Stored FX asset payload exceeds the configured size limit.");

        var sourceHeader = headerBytes.ToArray();
        var sourceStoredAsset = storedAssetBytes.ToArray();
        var wasCompressed = BinaryMagic.IsWad(sourceStoredAsset);
        var sourceAsset = wasCompressed
            ? WadCompression.Decompress(
                sourceStoredAsset,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken)
            : sourceStoredAsset.ToArray();
        if (sourceAsset.Length > options.MaximumDecompressedAssetBytes)
            throw new InvalidDataException("Decompressed FX asset payload exceeds the configured size limit.");

        var source = FxTextureInventoryReader.Read(sourceHeader, sourceAsset, cancellationToken);
        if (replacements.Count == 0 && additions.Count == 0)
            return new(sourceHeader, sourceStoredAsset, true);

        if ((long)source.Count + additions.Count > FxTextureInventoryReader.MaximumTextureCount)
            throw new InvalidDataException("FX texture additions exceed the texture-count limit.");
        var duplicate = replacements.GroupBy(value => value.Index).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"FX texture index {duplicate.Key} has more than one replacement.");
        foreach (var replacement in replacements)
        {
            if (replacement.Index < 0 || replacement.Index >= source.Count)
                throw new InvalidDataException($"FX replacement index {replacement.Index} is outside the source inventory.");
            if (!source[replacement.Index].IsValid)
                throw new InvalidDataException($"FX replacement index {replacement.Index} does not identify a valid source texture.");
            ValidateTexture(replacement.Texture, $"FX replacement {replacement.Index}");
        }
        for (var index = 0; index < additions.Count; index++)
            ValidateTexture(additions[index], $"FX addition {index}");

        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(sourceHeader.AsSpan(FxTextureInventoryReader.DataOffset));
        var cursor = (long)sourceAsset.Length;
        var plans = new List<PlannedTexture>(replacements.Count + additions.Count);
        foreach (var replacement in replacements.OrderBy(value => value.Index))
            plans.Add(Plan(replacement.Index, replacement.Texture, dataOffset, ref cursor, options));
        for (var index = 0; index < additions.Count; index++)
            plans.Add(Plan(source.Count + index, additions[index], dataOffset, ref cursor, options));

        cancellationToken.ThrowIfCancellationRequested();
        var outputAsset = new byte[checked((int)cursor)];
        sourceAsset.CopyTo(outputAsset, 0);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paletteAbsolute = checked(dataOffset + plan.PaletteOffset);
            var pixelAbsolute = checked(dataOffset + plan.PixelOffset);
            plan.Texture.PaletteBytes.Span.CopyTo(outputAsset.AsSpan(paletteAbsolute));
            plan.Texture.PixelBytes.Span.CopyTo(outputAsset.AsSpan(pixelAbsolute));
        }

        var outputHeader = BuildHeader(sourceHeader, source.Count, replacements, additions, plans, options);
        var outputStoredAsset = wasCompressed
            ? WadCompression.CompressVerified(
                outputAsset,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken).CompressedBytes
            : outputAsset;
        if (outputStoredAsset.Length > options.MaximumStoredAssetBytes)
            throw new InvalidDataException("Composed FX asset payload exceeds the configured stored-size limit.");

        Verify(
            sourceHeader,
            sourceAsset,
            source,
            outputHeader,
            outputStoredAsset,
            wasCompressed,
            replacements,
            additions,
            options,
            cancellationToken);
        return new(outputHeader, outputStoredAsset, false);
    }

    private static PlannedTexture Plan(
        int index,
        FxIndexedTexture texture,
        int dataOffset,
        ref long cursor,
        FxTextureCompositionOptions options)
    {
        cursor = checked(dataOffset + Align(cursor - dataOffset, FxTextureInventoryReader.PaletteAlignment));
        var paletteOffset = checked((int)(cursor - dataOffset));
        cursor = checked(cursor + FxTextureInventoryReader.PaletteLength);
        cursor = checked(dataOffset + Align(cursor - dataOffset, FxTextureInventoryReader.PixelAlignment));
        var pixelOffset = checked((int)(cursor - dataOffset));
        cursor = checked(cursor + texture.PixelBytes.Length);
        if (cursor > options.MaximumDecompressedAssetBytes)
            throw new InvalidDataException("Composed FX asset payload exceeds the configured decompressed-size limit.");
        return new(index, texture, paletteOffset, pixelOffset);
    }

    private static byte[] BuildHeader(
        byte[] sourceHeader,
        int sourceCount,
        IReadOnlyList<FxTextureReplacement> replacements,
        IReadOnlyList<FxIndexedTexture> additions,
        IReadOnlyList<PlannedTexture> plans,
        FxTextureCompositionOptions options)
    {
        var sourceDefinitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(
            sourceHeader.AsSpan(FxTextureInventoryReader.DefinitionsOffset));
        var definitionsOffset = sourceDefinitionsOffset;
        byte[] output;
        if (additions.Count == 0)
        {
            output = sourceHeader.ToArray();
        }
        else
        {
            definitionsOffset = checked((int)Align(sourceHeader.Length, 4));
            var length = checked(definitionsOffset
                + checked((sourceCount + additions.Count) * FxTextureInventoryReader.DefinitionLength));
            if (length > options.MaximumHeaderBytes)
                throw new InvalidDataException("Composed FX asset header exceeds the configured size limit.");
            output = new byte[length];
            sourceHeader.CopyTo(output, 0);
            sourceHeader.AsSpan(
                sourceDefinitionsOffset,
                checked(sourceCount * FxTextureInventoryReader.DefinitionLength))
                .CopyTo(output.AsSpan(definitionsOffset));
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(FxTextureInventoryReader.CountOffset), sourceCount + additions.Count);
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(FxTextureInventoryReader.DefinitionsOffset), definitionsOffset);
        }

        foreach (var plan in plans)
            WriteDefinition(
                output.AsSpan(
                    checked(definitionsOffset + plan.Index * FxTextureInventoryReader.DefinitionLength),
                    FxTextureInventoryReader.DefinitionLength),
                plan);
        return output;
    }

    private static void WriteDefinition(Span<byte> destination, PlannedTexture plan)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, plan.PaletteOffset);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], plan.PixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], plan.Texture.Width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], plan.Texture.Height);
    }

    private static void Verify(
        byte[] sourceHeader,
        byte[] sourceAsset,
        IReadOnlyList<FxTextureSourceEntry> source,
        byte[] outputHeader,
        byte[] outputStoredAsset,
        bool wasCompressed,
        IReadOnlyList<FxTextureReplacement> replacements,
        IReadOnlyList<FxIndexedTexture> additions,
        FxTextureCompositionOptions options,
        CancellationToken cancellationToken)
    {
        var outputAsset = wasCompressed
            ? WadCompression.Decompress(
                outputStoredAsset,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken)
            : outputStoredAsset;
        if (!outputAsset.AsSpan(0, sourceAsset.Length).SequenceEqual(sourceAsset))
            throw new InvalidDataException("FX composition changed existing asset payload bytes.");

        var output = FxTextureInventoryReader.Read(outputHeader, outputAsset, cancellationToken);
        if (output.Count != source.Count + additions.Count)
            throw new InvalidDataException("FX composition produced an unexpected texture count.");
        var replacementByIndex = replacements.ToDictionary(value => value.Index);
        var sourceDefinitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(
            sourceHeader.AsSpan(FxTextureInventoryReader.DefinitionsOffset));
        var outputDefinitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(
            outputHeader.AsSpan(FxTextureInventoryReader.DefinitionsOffset));
        for (var index = 0; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (replacementByIndex.TryGetValue(index, out var replacement))
            {
                VerifyTexture(output[index], replacement.Texture, $"FX replacement {index}");
                continue;
            }
            var sourceDefinition = sourceHeader.AsSpan(
                sourceDefinitionsOffset + index * FxTextureInventoryReader.DefinitionLength,
                FxTextureInventoryReader.DefinitionLength);
            var outputDefinition = outputHeader.AsSpan(
                outputDefinitionsOffset + index * FxTextureInventoryReader.DefinitionLength,
                FxTextureInventoryReader.DefinitionLength);
            if (!sourceDefinition.SequenceEqual(outputDefinition))
                throw new InvalidDataException($"FX source definition {index} changed during composition.");
        }
        for (var index = 0; index < additions.Count; index++)
            VerifyTexture(output[source.Count + index], additions[index], $"FX addition {index}");
    }

    private static void VerifyTexture(FxTextureSourceEntry actual, FxIndexedTexture expected, string label)
    {
        if (!actual.IsValid)
            throw new InvalidDataException($"{label} failed semantic re-read validation: {actual.Diagnostic}");
        if (actual.Width != expected.Width
            || actual.Height != expected.Height
            || !actual.PaletteBytes.AsSpan().SequenceEqual(expected.PaletteBytes.Span)
            || !actual.PixelBytes.AsSpan().SequenceEqual(expected.PixelBytes.Span))
            throw new InvalidDataException($"{label} failed semantic re-read validation.");
    }

    private static void ValidateTexture(FxIndexedTexture texture, string label)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.Width is <= 0 or > FxTextureInventoryReader.MaximumDimension
            || texture.Height is <= 0 or > FxTextureInventoryReader.MaximumDimension
            || !BitOperations.IsPow2((uint)texture.Width)
            || !BitOperations.IsPow2((uint)texture.Height))
            throw new InvalidDataException(
                $"{label} dimensions must be positive powers of two no larger than {FxTextureInventoryReader.MaximumDimension}.");
        if (texture.PaletteBytes.Length != FxTextureInventoryReader.PaletteLength)
            throw new InvalidDataException($"{label} must contain one 256-color RGBA32 palette.");
        if (texture.PixelBytes.Length != checked(texture.Width * texture.Height))
            throw new InvalidDataException($"{label} indexed-pixel length does not match its dimensions.");
    }

    private static void ValidateOptions(FxTextureCompositionOptions options)
    {
        if (options.MaximumHeaderBytes < FxTextureInventoryReader.HeaderLength
            || options.MaximumStoredAssetBytes < 0
            || options.MaximumDecompressedAssetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "FX composition size limits are invalid.");
    }

    private static long Align(long value, int alignment) =>
        checked((value + alignment - 1) & ~(long)(alignment - 1));
}
