using Microsoft.JSInterop;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.UYA.Level;
using System.Runtime.Versioning;

namespace RatchetPs2.Wasm;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    [JSInvokable("ConvertPifToPng")]
    public static byte[] ConvertPifToPng(byte[] pifBytes, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifBytes);

        var texture = PifReader.Read(pifBytes);
        var format = ParseTexturePixelFormat(pngFormat);

        return TextureConverter.ConvertToPng(
            texture,
            format,
            new TextureConversionOptions
            {
                DoubleAlpha = doubleAlpha,
            });
    }

    [JSInvokable("ConvertPifListToPng")]
    public static byte[][] ConvertPifListToPng(byte[][] pifImages, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifImages);

        var format = ParseTexturePixelFormat(pngFormat);
        var options = new TextureConversionOptions
        {
            DoubleAlpha = doubleAlpha,
        };

        return PifAssetExporter
            .ExportMany(pifImages, format, options)
            .Select(result => result.PngBytes)
            .ToArray();
    }

    [JSInvokable("ConvertPifListToPngPacked")]
    public static PifPackedBatchResult ConvertPifListToPngPacked(byte[][] pifImages, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifImages);

        var format = ParseTexturePixelFormat(pngFormat);
        var options = new TextureConversionOptions
        {
            DoubleAlpha = doubleAlpha,
        };

        return PifAssetExporter.ExportManyPacked(pifImages, format, options);
    }

    [JSInvokable("UnpackDlLevelWad")]
    public static PackedFilePackage UnpackDlLevelWad(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return DlLevelWadUnpacker.UnpackPacked(levelWadBytes);
    }

    [JSInvokable("UnpackUyaLevelWad")]
    public static PackedFilePackage UnpackUyaLevelWad(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return UyaLevelWadUnpacker.UnpackPacked(levelWadBytes);
    }

    [JSInvokable("BuildDlLevelWadRenderPackage")]
    public static PackedFilePackage BuildDlLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return DlLevelWadRenderPackageBuilder.BuildPacked(
            levelWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser);
    }

    [JSInvokable("BuildUyaLevelWadRenderPackage")]
    public static PackedFilePackage BuildUyaLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var renderFiles = BuildUyaRenderFiles(
            package.LevelWad.Level,
            package.Files);
        return PackRenderFiles(renderFiles, package.Files);
    }

    [JSInvokable("BuildGcLevelWadRenderPackage")]
    public static PackedFilePackage BuildGcLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var renderFiles = BuildUyaRenderFiles(
            package.LevelWad.Level,
            package.Files,
            GameId.GC);
        return PackRenderFiles(renderFiles, package.Files);
    }

    [JSInvokable("BuildUyaCustomMapZipRenderPackage")]
    public static PackedFilePackage BuildUyaCustomMapZipRenderPackage(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        var package = UyaCustomMapZipUnpacker.Unpack(zipBytes);
        var renderFiles = BuildUyaRenderFiles(levelIndex: 0, package.Files);
        return PackRenderFiles(renderFiles, package.Files);
    }

    private static IReadOnlyList<PackedFile> BuildUyaRenderFiles(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles,
        GameId gameId = GameId.UYA)
    {
        var renderFiles = UyaLevelWadRenderPackageBuilder.BuildFiles(
            levelIndex,
            unpackedFiles,
            assetFiles => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
                gameId,
                levelIndex,
                assetFiles.HeaderBytes,
                assetFiles.PaletteBytes,
                assetFiles.AssetWadBytes,
                DlLevelWadRenderPackageBuildOptions.Browser,
                assetFiles.ChunkWads,
                gameId == GameId.GC ? GcSkyRotationReader.ReadRadiansPerFrame(assetFiles.CodeBytes) : null),
            gameId);
        var hudHeader = unpackedFiles.FirstOrDefault(file => file.Path == "hud/header.bin");
        if (hudHeader is null)
        {
            return renderFiles;
        }

        var hudBanks = Enumerable.Range(0, HudBankReader.BankCount)
            .Select(index => unpackedFiles.FirstOrDefault(file => file.Path == $"hud/bank{index}.bin")?.Bytes ?? [])
            .ToArray();
        return renderFiles
            .Concat(HudBankRenderPackageBuilder.BuildFiles(hudHeader.Bytes, hudBanks))
            .ToArray();
    }

    [JSInvokable("ExportMobyGltf")]
    public static PackedFilePackage ExportMobyGltf(byte[] mobyBytes, string? game = null, bool skipAnimations = false, int? lod = null)
    {
        ArgumentNullException.ThrowIfNull(mobyBytes);

        using var input = new MemoryStream(mobyBytes, writable: false);
        var (animationFormat, modelFormat) = ParseMobyFormats(game);
        var options = new MobyGltfExportOptions
        {
            AnimationFormat = animationFormat,
            ModelFormat = modelFormat,
            SkipAnimationSequences = skipAnimations,
            LodIndex = lod,
            BufferFileName = "moby.buffer.bin"
        };
        var export = animationFormat == MobyAnimationFormat.Compact
            ? DlMobyGltfExporter.Export(input, "moby.gltf", options)
            : MobyGltfExporter.Export(input, "moby.gltf", options);

        return PackFiles(
            new PackedFile("moby.gltf", export.GltfBytes, "model/gltf+json"),
            new PackedFile("moby.buffer.bin", export.BinBytes, "application/octet-stream"),
            new PackedFile("moby.diagnostics.json", export.DiagnosticsBytes, "application/json"));
    }

    [JSInvokable("GetApiVersion")]
    public static string GetApiVersion() => "1";

    private static (MobyAnimationFormat Animation, MobyModelFormat Model) ParseMobyFormats(string? game)
    {
        return game?.Trim().ToUpperInvariant() switch
        {
            null or "" or "DL" => (MobyAnimationFormat.Compact, MobyModelFormat.Standard),
            "RC1" => (MobyAnimationFormat.Standard, MobyModelFormat.Rc1),
            "UYA" => (MobyAnimationFormat.Standard, MobyModelFormat.Standard),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Expected RC1, DL, or UYA."),
        };
    }

    private static TexturePixelFormat ParseTexturePixelFormat(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "rgba32" => TexturePixelFormat.Rgba32,
            "indexed8" => TexturePixelFormat.Indexed8,
            "indexed4" => TexturePixelFormat.Indexed4,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Expected rgba32, indexed8, or indexed4."),
        };
    }

    private static PackedFilePackage PackFiles(params PackedFile[] files)
    {
        return PackedFilePackageBuilder.Pack(files);
    }

    private static bool IsGameplayMetadataFile(PackedFile file)
    {
        return string.Equals(file.Path, "gameplay/gameplay_core.bin", StringComparison.Ordinal)
            || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
    }

    private static PackedFilePackage PackRenderFiles(
        IEnumerable<PackedFile> renderFiles,
        IEnumerable<PackedFile> sourceFiles)
    {
        return PackedFilePackageBuilder.Pack(
            renderFiles.Concat(sourceFiles.Where(IsGameplayMetadataFile)).ToArray());
    }
}
