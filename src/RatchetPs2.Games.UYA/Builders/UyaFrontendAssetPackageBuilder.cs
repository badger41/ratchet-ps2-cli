using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaFrontendAssetPackageBuilder
{
    public static PackedFilePackage Build(
        FrontendAssetKind kind,
        byte[] modelBytes,
        IReadOnlyList<FrontendAssetTexture> textures)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        ArgumentNullException.ThrowIfNull(textures);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (modelBytes.Length == 0) throw new ArgumentException("Asset model bytes cannot be empty.", nameof(modelBytes));
        if (textures.Any(texture => texture is null || texture.Role > 1 || texture.PifBytes is null))
            throw new ArgumentException("Asset textures contain an invalid entry.", nameof(textures));
        if (textures.Count(texture => texture.Role == 1) > 1)
            throw new ArgumentException("An asset can contain at most one billboard texture.", nameof(textures));

        var files = new List<PackedFile>();
        var uris = new Dictionary<int, string>();
        var sizes = new Dictionary<int, TextureSize>();
        var alpha = new Dictionary<int, TextureAlphaInfo>();
        TextureResource? billboard = null;
        var textureIndex = 0;
        foreach (var texture in textures)
        {
            var exported = PifAssetExporter.Export(texture.PifBytes);
            var resource = new TextureResource(
                texture.Role == 1 ? "textures/billboard.png" : $"textures/tex.{textureIndex:0000}.png",
                new(exported.Texture.Header.USize, exported.Texture.Header.VSize),
                TextureConverter.AnalyzeAlpha(TextureConverter.Decode(exported.Texture)));
            files.Add(new(resource.Path, exported.PngBytes, "image/png"));
            if (texture.Role == 1)
            {
                billboard = resource;
                continue;
            }
            uris[textureIndex] = resource.Path;
            sizes[textureIndex] = resource.Size;
            alpha[textureIndex] = resource.Alpha;
            textureIndex++;
        }

        using var input = new MemoryStream(modelBytes, writable: false);
        var (gltf, buffer) = kind switch
        {
            FrontendAssetKind.Moby => ExportMoby(input, uris, sizes, alpha),
            FrontendAssetKind.Tie => ExportTie(input, uris, sizes, alpha),
            FrontendAssetKind.Shrub => ExportShrub(input, uris, sizes, alpha, billboard),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        files.Add(new("model.gltf", gltf, "model/gltf+json"));
        files.Add(new("model.buffer.bin", buffer, "application/octet-stream"));
        return PackedFilePackageBuilder.Pack(files);
    }

    private static (byte[] Gltf, byte[] Buffer) ExportMoby(
        Stream input,
        IReadOnlyDictionary<int, string> uris,
        IReadOnlyDictionary<int, TextureSize> sizes,
        IReadOnlyDictionary<int, TextureAlphaInfo> alpha)
    {
        var export = MobyGltfExporter.Export(input, "model.gltf", new()
        {
            BufferFileName = "model.buffer.bin",
            LodIndex = 0,
            SkipAnimationSequences = true,
            ExternalTextureUris = uris,
            ExternalTextureSizes = sizes,
            ExternalTextureAlpha = alpha,
        });
        return (export.GltfBytes, export.BinBytes);
    }

    private static (byte[] Gltf, byte[] Buffer) ExportTie(
        Stream input,
        IReadOnlyDictionary<int, string> uris,
        IReadOnlyDictionary<int, TextureSize> sizes,
        IReadOnlyDictionary<int, TextureAlphaInfo> alpha)
    {
        var export = TieGltfExporter.Export(input, "model.gltf", new()
        {
            BufferFileName = "model.buffer.bin",
            GameProfile = TieGameProfile.ForGame(GameId.UYA),
            ExternalTextureUris = uris,
            ExternalTextureSizes = sizes,
            ExternalTextureAlpha = alpha,
            IncludeDiagnostics = false,
            Minify = true,
            MetadataMode = GltfExportMetadataMode.RuntimeOnly,
        });
        return (export.GltfBytes, export.BinBytes);
    }

    private static (byte[] Gltf, byte[] Buffer) ExportShrub(
        Stream input,
        IReadOnlyDictionary<int, string> uris,
        IReadOnlyDictionary<int, TextureSize> sizes,
        IReadOnlyDictionary<int, TextureAlphaInfo> alpha,
        TextureResource? billboard)
    {
        var export = ShrubGltfExporter.Export(input, "model.gltf", new()
        {
            BufferFileName = "model.buffer.bin",
            GameLabel = "UYA",
            ExternalTextureUris = uris,
            ExternalTextureSizes = sizes,
            ExternalTextureAlpha = alpha,
            ExternalBillboardTextureUri = billboard?.Path,
            ExternalBillboardTextureSize = billboard?.Size,
            ExternalBillboardTextureAlpha = billboard?.Alpha,
            IncludeDiagnostics = false,
            Minify = true,
            MetadataMode = GltfExportMetadataMode.RuntimeOnly,
        });
        return (export.GltfBytes, export.BinBytes);
    }

    private sealed record TextureResource(string Path, TextureSize Size, TextureAlphaInfo Alpha);
}
