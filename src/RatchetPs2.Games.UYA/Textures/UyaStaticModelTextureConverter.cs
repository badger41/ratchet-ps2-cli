using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.UYA.Textures;

internal static class UyaStaticModelTextureConverter
{

    public static StaticModelTextureConversionResult Convert(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken) => Convert(asset, int.MaxValue, cancellationToken);

    public static StaticModelTextureConversionResult Convert(
        StaticModelCanonicalAsset asset,
        int maximumTextureDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.FormatVersion != StaticModelCanonicalFormat.Version)
            throw new InvalidDataException($"Unsupported canonical static-model version {asset.FormatVersion}.");
        if (asset.Primitives is null || asset.Samplers is null || asset.Textures is null
            || asset.Materials is null || asset.Images is null)
            throw new InvalidDataException("The canonical static model has a missing collection.");
        if (asset.Primitives.Count == 0)
            throw new InvalidDataException("The canonical static model has no primitives.");

        var diagnostics = new List<StaticModelNativeDiagnostic>();
        var textureResult = Build(
            asset, PrepareMaterials(asset, diagnostics), diagnostics, maximumTextureDimension, cancellationToken);
        return new(
            GameId.UYA,
            textureResult.Textures,
            textureResult.Materials.Select(material => new StaticModelNativeMaterialBinding(
                material.NativeIndex,
                material.SourceIndex,
                material.Texture.AssetId,
                material.ClampU,
                material.ClampV)).ToArray(),
            diagnostics);
    }

    internal static PreparedMaterial[] PrepareMaterials(
        StaticModelCanonicalAsset asset,
        List<StaticModelNativeDiagnostic> diagnostics) =>
        UyaStaticModelMaterialResolver.PrepareUsed(asset, diagnostics);

    internal static TextureBuildResult Build(
        StaticModelCanonicalAsset asset,
        IReadOnlyList<PreparedMaterial> materials,
        List<StaticModelNativeDiagnostic> diagnostics,
        int maximumTextureDimension,
        CancellationToken cancellationToken)
    {
        if (maximumTextureDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextureDimension));
        var effectiveBySource = new Dictionary<EffectiveTextureSourceKey, EffectiveTexture>();
        var effectiveByHash = new Dictionary<string, EffectiveTexture>(StringComparer.Ordinal);
        var effectiveByMaterial = new EffectiveTexture[materials.Count];
        foreach (var material in materials)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceKey = new EffectiveTextureSourceKey(
                material.ImageIndex,
                material.BaseColor,
                material.AlphaMode,
                material.AlphaCutoff);
            if (!effectiveBySource.TryGetValue(sourceKey, out var texture))
            {
                if (material.ImageIndex is { } imageIndex)
                {
                    var image = asset.Images[imageIndex];
                    if (Math.Max(image.Width, image.Height) > maximumTextureDimension)
                        diagnostics.Add(new(
                            "texture-resized",
                            $"Texture {imageIndex} was resized from {image.Width}x{image.Height} "
                            + $"to {maximumTextureDimension}x{maximumTextureDimension} for native terrain streaming.",
                            SourceMaterialIndex: material.SourceIndex));
                }
                texture = BuildEffectiveTexture(asset, material, maximumTextureDimension, cancellationToken);
                effectiveBySource.Add(sourceKey, texture);
            }
            if (!effectiveByHash.TryGetValue(texture.Key, out var shared))
            {
                shared = texture;
                effectiveByHash.Add(texture.Key, texture);
            }
            effectiveByMaterial[material.NativeIndex] = shared;
        }

        var inputs = effectiveByHash.Values.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new Rgba32TexturePaletteInput(
                value.Key,
                PifTextureEncoding.Indexed8,
                0,
                0,
                256,
                [value.Rgba, .. value.MipRgba]))
            .ToArray();
        var emptyInventory = new TextureInventory(TextureInventoryBuilder.SchemaVersion, [], 0);
        var optimization = PaletteOptimizer.Optimize(
            emptyInventory, inputs, PaletteOptimizationProfile.Default, cancellationToken);
        if (optimization.Violations.Count > 0)
            throw new InvalidDataException(string.Join(' ', optimization.Violations.Select(value => value.Message)));

        var palettes = optimization.Palettes.ToDictionary(value => value.PaletteIndex);
        var assignments = optimization.CustomAssignments.ToDictionary(value => value.TextureKey, StringComparer.Ordinal);
        var nativeByKey = new Dictionary<string, StaticModelNativeTexture>(StringComparer.Ordinal);
        foreach (var effective in effectiveByHash.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var assignment = assignments[effective.Key];
            var palette = palettes[assignment.PaletteIndex];
            var paletteBytes = new byte[0x400];
            foreach (var entry in palette.Entries)
            {
                var offset = entry.PaletteIndex * 4;
                paletteBytes[offset] = entry.Color.Red;
                paletteBytes[offset + 1] = entry.Color.Green;
                paletteBytes[offset + 2] = entry.Color.Blue;
                paletteBytes[offset + 3] = entry.Color.Alpha;
            }
            var pixelOffset = 0;
            var levels = new[] { effective.Rgba }.Concat(effective.MipRgba)
                .Select(level =>
                {
                    var texelCount = level.Length / 4;
                    var pixels = new byte[texelCount];
                    for (var index = 0; index < texelCount; index++)
                        pixels[index] = TextureConverter.DecodePaletteIndex(
                            checked((byte)assignment.PixelIndices[pixelOffset + index]));
                    pixelOffset += texelCount;
                    return pixels;
                })
                .ToArray();
            if (pixelOffset != assignment.PixelIndices.Count)
                throw new InvalidDataException("Generated texture mip allocation does not match its palette assignment.");
            var pifBytes = PifWriter.Write(PifWriter.CreateIndexed8(
                effective.Width, effective.Height, paletteBytes, levels[0], levels[1..]));
            var assetId = $"sha256:{Hash(pifBytes).ToLowerInvariant()}";
            _ = PifReader.Read(pifBytes);
            nativeByKey.Add(effective.Key, new(
                assetId, effective.Width, effective.Height, assignment.PaletteIndex, pifBytes));
        }

        diagnostics.AddRange(optimization.Warnings.Select(message =>
            new StaticModelNativeDiagnostic("palette-optimization", message)));
        var boundMaterials = materials.Select(material => new BoundMaterial(
            material.NativeIndex,
            material.SourceIndex,
            material.ClampU,
            material.ClampV,
            nativeByKey[effectiveByMaterial[material.NativeIndex].Key])).ToArray();
        var nativeTextures = nativeByKey.Values
            .GroupBy(value => value.AssetId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                if (group.Skip(1).Any(value => !value.PifBytes.SequenceEqual(first.PifBytes)))
                    throw new InvalidDataException(
                        $"Generated textures have conflicting payloads for content ID {group.Key}.");
                return first;
            })
            .OrderBy(value => value.AssetId, StringComparer.Ordinal)
            .ToArray();
        return new(nativeTextures, boundMaterials);
    }

    private static EffectiveTexture BuildEffectiveTexture(
        StaticModelCanonicalAsset asset,
        PreparedMaterial material,
        int maximumTextureDimension,
        CancellationToken cancellationToken)
    {
        var width = 1;
        var height = 1;
        byte[] source = [255, 255, 255, 255];
        if (material.ImageIndex is { } imageIndex)
        {
            var image = asset.Images[imageIndex];
            if (image.Width is <= 0 or > 4096 || image.Height is <= 0 or > 4096
                || image.Rgba32 is null || image.Rgba32.Length != checked(image.Width * image.Height * 4))
                throw new InvalidDataException($"Source image {imageIndex} has invalid dimensions or RGBA data.");
            width = image.Width;
            height = image.Height;
            source = image.Rgba32;
        }
        if (width != height || width > maximumTextureDimension)
        {
            var size = Math.Min(Math.Max(width, height), maximumTextureDimension);
            var square = new byte[checked(size * size * 4)];
            for (var y = 0; y < size; y++)
            {
                if ((y & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
                var sourceY = Math.Min(height - 1, y * height / size);
                for (var x = 0; x < size; x++)
                {
                    var sourceX = Math.Min(width - 1, x * width / size);
                    source.AsSpan((sourceY * width + sourceX) * 4, 4)
                        .CopyTo(square.AsSpan((y * size + x) * 4));
                }
            }
            width = height = size;
            source = square;
        }

        var rgba = new byte[source.Length];
        for (var offset = 0; offset < source.Length; offset += 4)
        {
            if ((offset & 0x3ffff) == 0) cancellationToken.ThrowIfCancellationRequested();
            rgba[offset] = Multiply(source[offset], material.BaseColor.X);
            rgba[offset + 1] = Multiply(source[offset + 1], material.BaseColor.Y);
            rgba[offset + 2] = Multiply(source[offset + 2], material.BaseColor.Z);
            var alpha = material.AlphaMode == "OPAQUE"
                ? 1f
                : source[offset + 3] / 255f * material.BaseColor.W;
            if (material.AlphaMode == "MASK") alpha = alpha >= material.AlphaCutoff ? 1f : 0f;
            rgba[offset + 3] = checked((byte)MathF.Round(alpha * Ps2Color.FullOpacityAlpha));
        }
        var mipRgba = BuildMipChain(rgba, width, height, 2, cancellationToken);
        var identity = new byte[8 + rgba.Length];
        BinaryPrimitives.WriteInt32LittleEndian(identity, width);
        BinaryPrimitives.WriteInt32LittleEndian(identity.AsSpan(4), height);
        rgba.CopyTo(identity, 8);
        return new(Hash(identity), width, height, rgba, mipRgba);
    }

    private static IReadOnlyList<byte[]> BuildMipChain(
        byte[] baseRgba,
        int width,
        int height,
        int count,
        CancellationToken cancellationToken)
    {
        var result = new byte[count][];
        var source = baseRgba;
        for (var level = 0; level < count; level++)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            var next = new byte[checked(nextWidth * nextHeight * 4)];
            for (var y = 0; y < nextHeight; y++)
            {
                if ((y & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < nextWidth; x++)
                {
                    var weightedRed = 0;
                    var weightedGreen = 0;
                    var weightedBlue = 0;
                    var totalAlpha = 0;
                    var samples = 0;
                    for (var sourceY = y * 2; sourceY < Math.Min(height, y * 2 + 2); sourceY++)
                    {
                        for (var sourceX = x * 2; sourceX < Math.Min(width, x * 2 + 2); sourceX++)
                        {
                            var sourceOffset = (sourceY * width + sourceX) * 4;
                            var alpha = source[sourceOffset + 3];
                            weightedRed += source[sourceOffset] * alpha;
                            weightedGreen += source[sourceOffset + 1] * alpha;
                            weightedBlue += source[sourceOffset + 2] * alpha;
                            totalAlpha += alpha;
                            samples++;
                        }
                    }
                    var targetOffset = (y * nextWidth + x) * 4;
                    if (totalAlpha > 0)
                    {
                        next[targetOffset] = checked((byte)((weightedRed + totalAlpha / 2) / totalAlpha));
                        next[targetOffset + 1] = checked((byte)((weightedGreen + totalAlpha / 2) / totalAlpha));
                        next[targetOffset + 2] = checked((byte)((weightedBlue + totalAlpha / 2) / totalAlpha));
                    }
                    next[targetOffset + 3] = checked((byte)((totalAlpha + samples / 2) / samples));
                }
            }
            result[level] = next;
            source = next;
            width = nextWidth;
            height = nextHeight;
        }
        return result;
    }

    private static byte Multiply(byte value, float factor) =>
        checked((byte)Math.Clamp(MathF.Round(value * factor), 0, byte.MaxValue));

    private static string Hash(ReadOnlySpan<byte> bytes) => System.Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed record EffectiveTexture(
    string Key,
    int Width,
    int Height,
    byte[] Rgba,
    IReadOnlyList<byte[]> MipRgba);

internal readonly record struct EffectiveTextureSourceKey(
    int? ImageIndex,
    Vector4 BaseColor,
    string AlphaMode,
    float AlphaCutoff);

internal sealed record PreparedMaterial(
    int NativeIndex,
    int? SourceIndex,
    int? ImageIndex,
    Vector4 BaseColor,
    string AlphaMode,
    float AlphaCutoff,
    bool DoubleSided,
    bool ClampU,
    bool ClampV);

internal sealed record BoundMaterial(
    int NativeIndex,
    int? SourceIndex,
    bool ClampU,
    bool ClampV,
    StaticModelNativeTexture Texture);

internal sealed record TextureBuildResult(
    IReadOnlyList<StaticModelNativeTexture> Textures,
    IReadOnlyList<BoundMaterial> Materials);
