using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Textures;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static class UyaStaticModelTfragTextureConverter
{
    private const int ImportedTextureStreamingBudget = 768 * 1024;
    private const int MinimumTextureDimension = 8;
    private const int MaximumStreamingTextureCount = byte.MaxValue;
    private const int MaximumTerrainTextureCount = byte.MaxValue + 1;

    public static StaticModelTextureConversionResult Convert(
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken)
    {
        var maximumDimension = UyaStaticModelTfragPartitioner.MaximumTextureDimension;
        var converted = UyaStaticModelTextureConverter.Convert(asset, maximumDimension, cancellationToken);
        var streamingBytes = converted.Textures.Sum(TextureStreamingBytes);
        if (streamingBytes <= ImportedTextureStreamingBudget) return converted;

        maximumDimension = SelectTextureDimension(converted.Textures, maximumDimension);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            converted = UyaStaticModelTextureConverter.Convert(asset, maximumDimension, cancellationToken);
            streamingBytes = converted.Textures.Sum(TextureStreamingBytes);
            if (streamingBytes <= ImportedTextureStreamingBudget)
            {
                return converted with
                {
                    Diagnostics =
                    [
                        .. converted.Diagnostics,
                        new StaticModelNativeDiagnostic(
                            "texture-streaming-budget",
                            $"Terrain textures were limited to {maximumDimension}x{maximumDimension} "
                            + $"({streamingBytes:N0} streamed bytes) to stay within the UYA GS texture window."),
                    ],
                };
            }
            if (maximumDimension <= MinimumTextureDimension)
                throw new InvalidDataException(
                    $"Static-model tfrag textures require {streamingBytes:N0} streamed bytes; "
                    + $"the UYA imported-terrain budget is {ImportedTextureStreamingBudget:N0} bytes.");
            maximumDimension /= 2;
        }
    }

    public static void ValidateAllocation(int firstTextureId, int textureCount)
    {
        if (firstTextureId < 0) throw new ArgumentOutOfRangeException(nameof(firstTextureId));
        if (textureCount < 0) throw new ArgumentOutOfRangeException(nameof(textureCount));
        if (textureCount > MaximumStreamingTextureCount)
            throw new InvalidDataException(
                $"A UYA custom tfrag import supports at most {MaximumStreamingTextureCount} textures; "
                + $"the import needs {textureCount}.");
        if ((long)firstTextureId + textureCount > MaximumTerrainTextureCount)
            throw new InvalidDataException(
                $"UYA terrain has {firstTextureId} existing textures and only "
                + $"{Math.Max(0, MaximumTerrainTextureCount - firstTextureId)} custom texture ID(s) remain.");
    }

    private static int SelectTextureDimension(
        IReadOnlyList<StaticModelNativeTexture> textures,
        int maximumDimension)
    {
        while (maximumDimension > MinimumTextureDimension)
        {
            maximumDimension /= 2;
            var streamingBytes = textures.Sum(texture =>
            {
                var width = Math.Min(texture.Width, maximumDimension);
                var height = Math.Min(texture.Height, maximumDimension);
                return checked(width * height + Math.Max(1, width / 2) * Math.Max(1, height / 2));
            });
            if (streamingBytes <= ImportedTextureStreamingBudget) break;
        }
        return maximumDimension;
    }

    private static int TextureStreamingBytes(StaticModelNativeTexture texture)
    {
        var mipWidth = Math.Max(1, texture.Width / 2);
        var mipHeight = Math.Max(1, texture.Height / 2);
        return checked(texture.Width * texture.Height + mipWidth * mipHeight);
    }
}
