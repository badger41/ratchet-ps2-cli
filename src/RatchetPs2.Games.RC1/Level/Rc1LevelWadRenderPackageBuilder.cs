using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelWadRenderPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static PackedFilePackage BuildPacked(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles,
        Func<Rc1LevelAssetSourceFiles, IReadOnlyList<PackedFile>> buildAssetFiles) =>
        PackedFilePackageBuilder.Pack(BuildFiles(levelIndex, unpackedFiles, buildAssetFiles));

    public static IReadOnlyList<PackedFile> BuildFiles(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles,
        Func<Rc1LevelAssetSourceFiles, IReadOnlyList<PackedFile>> buildAssetFiles)
    {
        ArgumentNullException.ThrowIfNull(unpackedFiles);
        ArgumentNullException.ThrowIfNull(buildAssetFiles);

        var started = Stopwatch.GetTimestamp();
        var sourceFiles = unpackedFiles.ToDictionary(file => Normalize(file.Path), StringComparer.Ordinal);
        var files = new List<PackedFile>();
        var assetFiles = buildAssetFiles(GetAssetSources(sourceFiles));
        files.AddRange(assetFiles);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "RC1",
            ["Source"] = "loose_level_wad",
            ["RenderPackageVersion"] = 1,
            ["Level"] = levelIndex,
            ["UnpackedFileCount"] = unpackedFiles.Count
        };
        CopyMobyManifest(manifest, assetFiles);
        BuildHudFiles(files, sourceFiles);
        BuildWorldFiles(files, sourceFiles, manifest);
        manifest["PerformanceTimings"] = new[]
        {
            new RenderPackageTiming(
                "managed.before-pack",
                "Managed build before pack",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                $"{files.Count} files")
        };
        AddJson(files, "manifest.json", manifest);
        return files;
    }

    private static void BuildHudFiles(
        List<PackedFile> files,
        IReadOnlyDictionary<string, PackedFile> sourceFiles)
    {
        var header = TryGet(sourceFiles, "hud/header.bin");
        if (header is null)
        {
            return;
        }

        var banks = Enumerable.Range(0, HudBankReader.BankCount)
            .Select(index => TryGet(sourceFiles, $"hud/bank{index}.bin")?.Bytes ?? [])
            .ToArray();
        files.AddRange(HudBankRenderPackageBuilder.BuildFiles(header.Bytes, banks));
    }

    public static Rc1LevelAssetSourceFiles GetAssetSources(IReadOnlyList<PackedFile> unpackedFiles)
    {
        ArgumentNullException.ThrowIfNull(unpackedFiles);
        return GetAssetSources(unpackedFiles.ToDictionary(file => Normalize(file.Path), StringComparer.Ordinal));
    }

    private static void BuildWorldFiles(
        List<PackedFile> files,
        IReadOnlyDictionary<string, PackedFile> sourceFiles,
        IDictionary<string, object?> rootManifest)
    {
        var slots = new List<WorldSlotRoute>();
        AddWorldSlot(files, sourceFiles, slots, 0x04, "directional_lights", "directional_lights", "lighting/directional_lights.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x30, "tie_class_ids", "tie_classes", "tie/class_ids.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x34, "tie_instances", "tie_instances", "tie/instances.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x34, "tie_instance_colors", "tie_ambient_rgbas", "tie/colors.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x38, "shrub_class_ids", "shrub_classes", "shrub/class_ids.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x3c, "shrub_instances", "shrub_instances", "shrub/instances.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x78, "point_light_grid", "point_light_grid", "lighting/point_light_grid.bin");
        AddWorldSlot(files, sourceFiles, slots, 0x7c, "point_lights", "point_lights", "lighting/point_lights.bin");

        var worldManifest = new Dictionary<string, object?>
        {
            ["Length"] = TryGet(sourceFiles, "gameplay/gameplay_core.bin")?.Bytes.Length,
            ["Slots"] = slots,
            ["DirectionalLightCount"] = ReadCount(sourceFiles, "gameplay/core/directional_lights.bin"),
            ["TieClassCount"] = ReadCount(sourceFiles, "gameplay/core/tie_classes.bin"),
            ["TieInstanceCount"] = ReadCount(sourceFiles, "gameplay/core/tie_instances.bin"),
            ["ShrubClassCount"] = ReadCount(sourceFiles, "gameplay/core/shrub_classes.bin"),
            ["ShrubInstanceCount"] = ReadCount(sourceFiles, "gameplay/core/shrub_instances.bin"),
            ["PointLightCount"] = ReadCount(sourceFiles, "gameplay/core/point_lights.bin")
        };
        AddJson(files, "world/manifest.json", worldManifest);
        rootManifest["World"] = worldManifest;
    }

    private static void AddWorldSlot(
        List<PackedFile> files,
        IReadOnlyDictionary<string, PackedFile> sourceFiles,
        List<WorldSlotRoute> slots,
        int headerOffset,
        string semanticName,
        string sourceName,
        string relativePath)
    {
        var source = TryGet(sourceFiles, $"gameplay/core/{sourceName}.bin");
        if (source is null)
        {
            slots.Add(new WorldSlotRoute(slots.Count, headerOffset, 0, 0, semanticName, null, "empty"));
            return;
        }

        AddFile(files, $"world/{relativePath}", source.Bytes, source.ContentType);
        slots.Add(new WorldSlotRoute(slots.Count, headerOffset, 0, source.Bytes.Length, semanticName, relativePath, "mapped"));
    }

    private static void CopyMobyManifest(IDictionary<string, object?> manifest, IReadOnlyList<PackedFile> assetFiles)
    {
        var assetManifest = assetFiles.FirstOrDefault(file => file.Path == "assets/render_manifest.json");
        if (assetManifest is null)
        {
            return;
        }

        using var document = JsonDocument.Parse(assetManifest.Bytes);
        if (document.RootElement.TryGetProperty("Mobys", out var mobys))
        {
            manifest["Mobys"] = mobys.Clone();
        }
    }

    private static int? ReadCount(IReadOnlyDictionary<string, PackedFile> files, string path)
    {
        var file = TryGet(files, path);
        return file is null || file.Bytes.Length < sizeof(int)
            ? null
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(file.Bytes));
    }

    private static PackedFile Require(IReadOnlyDictionary<string, PackedFile> files, string path) =>
        TryGet(files, path) ?? throw new InvalidDataException($"RC1 level WAD is missing '{path}'.");

    private static Rc1LevelAssetSourceFiles GetAssetSources(IReadOnlyDictionary<string, PackedFile> files) => new(
        Require(files, "assets/asset_header.bin").Bytes,
        Require(files, "assets/palette.bin").Bytes,
        Require(files, "assets/asset_wad.bin").Bytes);

    private static PackedFile? TryGet(IReadOnlyDictionary<string, PackedFile> files, string path) =>
        files.TryGetValue(Normalize(path), out var file) ? file : null;

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static void AddJson(List<PackedFile> files, string path, object value) =>
        AddFile(files, path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json");

    private static void AddFile(List<PackedFile> files, string path, byte[] bytes, string? contentType = null)
    {
        if (bytes.Length > 0)
        {
            files.Add(new PackedFile(path, bytes, contentType ?? "application/octet-stream"));
        }
    }

    private sealed record WorldSlotRoute(
        int Index,
        int HeaderOffset,
        int Pointer,
        int Length,
        string SemanticName,
        string? Path,
        string Status);

    private sealed record RenderPackageTiming(string Key, string Label, double DurationMs, string? Detail);
}

public sealed record Rc1LevelAssetSourceFiles(byte[] HeaderBytes, byte[] PaletteBytes, byte[] AssetWadBytes);
