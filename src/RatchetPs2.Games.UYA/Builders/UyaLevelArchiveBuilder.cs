using System.Security.Cryptography;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaLevelArchiveBuilder
{
    public const int SchemaVersion = 1;
    public static LevelArchiveCapability Capability { get; } =
        new("UYA", "NTSC-U", ["1.00"], "uya-ntsc-u");

    public static bool SupportsTarget(string game, string region, string revision, string bakeProfile) =>
        game == Capability.Game
        && region == Capability.Region
        && Capability.Revisions.Contains(revision, StringComparer.Ordinal)
        && bakeProfile == Capability.BakeProfile;

    public static LevelArchiveBuildResult Build(
        ReadOnlyMemory<byte> source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null,
        LevelArchiveBuildOptions? options = null,
        IProgress<LevelArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildCore(source, replacements, options ?? new(), progress, cancellationToken);

    public static LevelArchiveBuildResult Build(
        Stream source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null,
        LevelArchiveBuildOptions? options = null,
        IProgress<LevelArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The UYA level archive stream must be readable and seekable.", nameof(source));
        if (source.Length > int.MaxValue)
            throw new InvalidDataException("The UYA level archive is too large to read in memory.");

        Report(progress, cancellationToken, LevelArchivePhase.Reading, 0, "Reading UYA level archive.");
        source.Position = 0;
        var bytes = source.ReadBytesExactly((int)source.Length);
        return BuildCore(bytes, replacements, options ?? new(), progress, cancellationToken);
    }

    private static LevelArchiveBuildResult BuildCore(
        ReadOnlyMemory<byte> source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements,
        LevelArchiveBuildOptions options,
        IProgress<LevelArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Decompression);
        var sourceHash = Hash(source.Span);
        byte[]? uncompressed = null;
        var warnings = new List<string>();
        var compressions = new List<LevelArchiveCompression>();

        try
        {
            Report(progress, cancellationToken, LevelArchivePhase.Inventory, 0.1, "Inventorying UYA containers.");
            var inventory = UyaLevelWadInventoryReader.Read(source);
            var userReplacements = CopyReplacements(replacements);
            RejectDirectContainerReplacements(inventory, userReplacements);

            Report(progress, cancellationToken, LevelArchivePhase.Rebuild, 0.3, "Rebuilding the uncompressed archive image.");
            uncompressed = UyaLevelWadWriter.Write(inventory, userReplacements);

            Report(progress, cancellationToken, LevelArchivePhase.Validate, 0.45, "Validating the uncompressed archive image.");
            UyaLevelWadInventoryReader.Read(uncompressed);
            if (options.RequireSourceEquality && !source.Span.SequenceEqual(uncompressed))
                throw new InvalidDataException("The uncompressed UYA rebuild is not byte-identical to its source.");

            Report(progress, cancellationToken, LevelArchivePhase.Compress, 0.6, "Compressing and verifying nested WAD payloads.");
            var output = BuildCompressedImage(inventory, userReplacements, options.Decompression, compressions, cancellationToken);
            var outputInventory = UyaLevelWadInventoryReader.Read(output);
            var changes = FindChanges(inventory, outputInventory);

            Report(progress, cancellationToken, LevelArchivePhase.Complete, 1, "UYA level archive build complete.");
            return new(
                true,
                SchemaVersion,
                GetSdkVersion(),
                output,
                source.Length,
                sourceHash,
                uncompressed.Length,
                Hash(uncompressed),
                output.Length,
                Hash(output),
                changes,
                compressions,
                warnings,
                []);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            progress?.Report(new(LevelArchivePhase.Failed, 1, exception.Message));
            return new(
                false,
                SchemaVersion,
                GetSdkVersion(),
                null,
                source.Length,
                sourceHash,
                uncompressed?.Length,
                uncompressed is null ? null : Hash(uncompressed),
                null,
                null,
                [],
                compressions,
                warnings,
                [new("UYA_ARCHIVE_BUILD_FAILED", exception.Message, true)]);
        }
    }

    private static byte[] BuildCompressedImage(
        UyaLevelWadInventory inventory,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> userReplacements,
        WadDecompressionOptions decompression,
        ICollection<LevelArchiveCompression> compressions,
        CancellationToken cancellationToken)
    {
        var containers = inventory.Containers.ToDictionary(container => container.Path, StringComparer.Ordinal);
        var effective = CopyReplacements(userReplacements);

        RebuildChild(containers, effective, "assets/asset_wad.bin", "level_wad/level_data.wad",
            "assets/asset_wad.bin", decompression, compressions, cancellationToken);
        RebuildChild(containers, effective, "gameplay/gameplay_core.bin", "level_wad",
            "gameplay/gameplay.bin", decompression, compressions, cancellationToken);
        RebuildChild(containers, effective, "level_wad/level_data.wad", "level_wad",
            "level_wad/level_data.wad", decompression, compressions, cancellationToken);

        if (!containers.TryGetValue("level_wad", out var root))
            throw new InvalidDataException("UYA level inventory is missing its level_wad root container.");
        RecompressSlots(root, effective,
            new HashSet<string>(["level_wad/level_data.wad", "gameplay/gameplay.bin"], StringComparer.Ordinal),
            decompression, compressions, cancellationToken);
        return UyaLevelWadWriter.WriteContainer(root, FilterReplacements(root, effective));
    }

    private static void RebuildChild(
        IReadOnlyDictionary<string, UyaContainerInventory> containers,
        Dictionary<string, ReadOnlyMemory<byte>> replacements,
        string childPath,
        string parentPath,
        string parentSlotPath,
        WadDecompressionOptions decompression,
        ICollection<LevelArchiveCompression> compressions,
        CancellationToken cancellationToken)
    {
        if (!containers.TryGetValue(childPath, out var child)) return;
        if (!containers.TryGetValue(parentPath, out var parent))
            throw new InvalidDataException($"UYA level inventory is missing parent container {parentPath}.");

        var skippedChildren = childPath == "level_wad/level_data.wad"
            ? new HashSet<string>(["assets/asset_wad.bin"], StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        RecompressSlots(child, replacements, skippedChildren, decompression, compressions, cancellationToken);
        var uncompressed = UyaLevelWadWriter.WriteContainer(child, FilterReplacements(child, replacements));
        ReadOnlyMemory<byte> encoded = uncompressed;
        if (child.SourceCompression == UyaContainerCompression.Wad)
        {
            var compression = WadCompression.CompressVerified(uncompressed, decompression, cancellationToken);
            compressions.Add(ToCompression(child.Path, compression));
            encoded = compression.CompressedBytes;
        }

        var parentSlot = FindSlot(parent, parentSlotPath);
        SetReplacement(replacements, parentSlot, encoded);
    }

    private static void RecompressSlots(
        UyaContainerInventory container,
        Dictionary<string, ReadOnlyMemory<byte>> replacements,
        IReadOnlySet<string> skippedPaths,
        WadDecompressionOptions decompression,
        ICollection<LevelArchiveCompression> compressions,
        CancellationToken cancellationToken)
    {
        foreach (var slot in container.Slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slot.Compression != UyaContainerCompression.Wad
                || slot.LogicalPaths.Any(skippedPaths.Contains)) continue;
            var replacement = ResolveReplacement(container.Path, slot, replacements);
            if (!replacement.HasValue) continue;
            var uncompressed = replacement.Bytes.ToArray();
            var compression = WadCompression.CompressVerified(uncompressed, decompression, cancellationToken);
            compressions.Add(ToCompression(slot.Path, compression));
            SetReplacement(replacements, slot, compression.CompressedBytes);
        }
    }

    private static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> FilterReplacements(
        UyaContainerInventory container,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> replacements)
    {
        var paths = container.Slots.SelectMany(slot => slot.LogicalPaths).ToHashSet(StringComparer.Ordinal);
        return replacements.Where(pair => paths.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void RejectDirectContainerReplacements(
        UyaLevelWadInventory inventory,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> replacements)
    {
        foreach (var container in inventory.Containers.Where(container => container.SourceContainerPath is not null))
        {
            var parent = inventory.Containers.Single(candidate => candidate.Path == container.SourceContainerPath);
            var parentSlot = FindSlot(parent, container.Path == "gameplay/gameplay_core.bin"
                ? "gameplay/gameplay.bin"
                : container.Path);
            if (parentSlot.LogicalPaths.Any(replacements.ContainsKey))
                throw new ArgumentException(
                    $"Replace payloads inside {container.Path} instead of replacing its encoded parent slot directly.",
                    nameof(replacements));
        }
    }

    private static ResolvedReplacement ResolveReplacement(
        string containerPath,
        UyaContainerSlot slot,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> replacements)
    {
        var result = new ResolvedReplacement(false, default);
        foreach (var path in slot.LogicalPaths)
        {
            if (!replacements.TryGetValue(path, out var bytes)) continue;
            if (result.HasValue && !result.Bytes.Span.SequenceEqual(bytes.Span))
                throw new ArgumentException($"{containerPath} aliases for {slot.Path} have conflicting replacements.");
            result = new(true, bytes);
        }
        return result;
    }

    private static void SetReplacement(
        Dictionary<string, ReadOnlyMemory<byte>> replacements,
        UyaContainerSlot slot,
        ReadOnlyMemory<byte> bytes)
    {
        foreach (var path in slot.LogicalPaths) replacements.Remove(path);
        replacements.Add(slot.Path, bytes);
    }

    private static UyaContainerSlot FindSlot(UyaContainerInventory container, string logicalPath) =>
        container.Slots.Single(slot => slot.LogicalPaths.Contains(logicalPath, StringComparer.Ordinal));

    private static Dictionary<string, ReadOnlyMemory<byte>> CopyReplacements(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements)
    {
        var copy = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        if (replacements is not null)
            foreach (var replacement in replacements) copy.Add(replacement.Key, replacement.Value);
        return copy;
    }

    private static IReadOnlyList<LevelArchiveChange> FindChanges(
        UyaLevelWadInventory source,
        UyaLevelWadInventory output)
    {
        var changes = new List<LevelArchiveChange>();
        var outputContainers = output.Containers.ToDictionary(container => container.Path, StringComparer.Ordinal);
        foreach (var sourceContainer in source.Containers)
        {
            if (!outputContainers.TryGetValue(sourceContainer.Path, out var outputContainer)) continue;
            var outputSlots = outputContainer.Slots.ToDictionary(slot => slot.Path, StringComparer.Ordinal);
            foreach (var sourceSlot in sourceContainer.Slots)
            {
                if (!outputSlots.TryGetValue(sourceSlot.Path, out var outputSlot)) continue;
                if (sourceSlot.Offset == outputSlot.Offset && sourceSlot.Length == outputSlot.Length
                    && sourceSlot.Sha256 == outputSlot.Sha256) continue;
                changes.Add(new(
                    sourceContainer.Path,
                    sourceSlot.Path,
                    sourceSlot.Offset,
                    sourceSlot.Length,
                    sourceSlot.Sha256,
                    outputSlot.Offset,
                    outputSlot.Length,
                    outputSlot.Sha256));
            }
        }
        return changes;
    }

    private static LevelArchiveCompression ToCompression(string path, WadCompressionResult result) =>
        new(path, result.UncompressedSize, result.CompressedSize,
            result.UncompressedSha256, result.CompressedSha256);

    private static void Report(
        IProgress<LevelArchiveProgress>? progress,
        CancellationToken cancellationToken,
        LevelArchivePhase phase,
        double completion,
        string message)
    {
        progress?.Report(new(phase, completion, message));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string GetSdkVersion() =>
        typeof(UyaLevelArchiveBuilder).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private readonly record struct ResolvedReplacement(bool HasValue, ReadOnlyMemory<byte> Bytes);
}
