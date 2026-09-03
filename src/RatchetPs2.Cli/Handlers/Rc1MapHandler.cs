using System.Text.Json;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Cli.Handlers;

internal static class Rc1MapHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Rc1MapExtractionSummary Extract(
        FileInfo isoFile,
        int levelId,
        DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(isoFile);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        if (!isoFile.Exists)
        {
            throw new FileNotFoundException("Input ISO does not exist.", isoFile.FullName);
        }

        outputDirectory.Create();
        using var isoStream = isoFile.OpenRead();
        var extracted = Rc1LooseLevelWadExtractor.ExtractAll(isoStream, levelId);
        var package = Rc1LevelWadUnpacker.Unpack(extracted.Level.Bytes);
        PackedFilePackageWriter.WriteFiles(package.Files, outputDirectory);

        var assetFiles = BuildAssetFiles(
            levelId,
            Rc1LevelWadRenderPackageBuilder.GetAssetSources(package.Files),
            DlLevelWadRenderPackageBuildOptions.Default);
        PackedFilePackageWriter.WriteFiles(assetFiles, outputDirectory);

        IReadOnlyList<PackedFile> hudFiles = [];
        var hudHeader = package.Files.FirstOrDefault(file => file.Path == "hud/header.bin");
        if (hudHeader is not null)
        {
            var hudBanks = Enumerable.Range(0, HudBankReader.BankCount)
                .Select(index => package.Files.FirstOrDefault(file => file.Path == $"hud/bank{index}.bin")?.Bytes ?? [])
                .ToArray();
            hudFiles = HudBankRenderPackageBuilder.BuildFiles(hudHeader.Bytes, hudBanks);
            PackedFilePackageWriter.WriteFiles(hudFiles, outputDirectory);
        }

        var optionalFileCount = 0;
        optionalFileCount += WriteOptional(outputDirectory.FullName, "level_audio/level_audio.wad", extracted.Audio);
        optionalFileCount += WriteOptional(outputDirectory.FullName, "level_scene/level_scene.wad", extracted.Scene);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "RC1",
            ["SourceIso"] = isoFile.FullName,
            ["RequestedLevelId"] = levelId,
            ["LevelTableIndex"] = extracted.Level.LevelInfo.TableIndex,
            ["AmalgamatedHeaderSector"] = extracted.Level.HeaderSector,
            ["LevelWad"] = extracted.Level.LevelWad,
            ["LevelWadSectorCount"] = extracted.Level.SectorCount,
            ["LevelWadByteLength"] = extracted.Level.ByteLength,
            ["LevelAudioWadByteLength"] = extracted.Audio.Length,
            ["LevelSceneWadByteLength"] = extracted.Scene.Length,
            ["UnpackedFileCount"] = package.Files.Count,
            ["AssetUnpackedFileCount"] = assetFiles.Count,
            ["HudRenderFileCount"] = hudFiles.Count
        };
        File.WriteAllText(
            Path.Combine(outputDirectory.FullName, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new Rc1MapExtractionSummary(
            outputDirectory.FullName,
            package.Files.Count + assetFiles.Count + hudFiles.Count + optionalFileCount + 1,
            extracted.Level.SectorCount);
    }

    public static MapWadExtractionResult ExtractWad(Stream isoStream, int levelId)
    {
        var wad = Rc1LooseLevelWadExtractor.ExtractPrimary(isoStream, levelId);
        return new(wad.Bytes, wad.SectorCount, wad.HeaderSector, wad.PayloadBaseSector);
    }

    public static IReadOnlyList<PackedFile> BuildFiles(byte[] levelWadBytes, bool render)
    {
        var package = Rc1LevelWadUnpacker.Unpack(levelWadBytes);
        if (!render)
        {
            return package.Files;
        }

        var options = DlLevelWadRenderPackageBuildOptions.Browser with
        {
            IncludeDiagnostics = true,
            MobyLodIndex = null
        };
        return Rc1LevelWadRenderPackageBuilder.BuildFiles(
            package.LevelWad.Level,
            package.Files,
            assets => BuildAssetFiles(package.LevelWad.Level, assets, options));
    }

    private static IReadOnlyList<PackedFile> BuildAssetFiles(
        int levelId,
        Rc1LevelAssetSourceFiles assets,
        DlLevelWadRenderPackageBuildOptions options) =>
        DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.RC1,
            levelId,
            assets.HeaderBytes,
            assets.PaletteBytes,
            assets.AssetWadBytes,
            options with { AssetProfile = Rc1LevelAssetProfile.Default });

    private static int WriteOptional(string outputRoot, string relativePath, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return 0;
        }

        var outputPath = Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, bytes);
        return 1;
    }
}

internal sealed record Rc1MapExtractionSummary(
    string OutputDirectory,
    int FileCount,
    int SectorCount);
