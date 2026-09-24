using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class UyaArchiveQualification
{
    public const int SchemaVersion = 1;

    public static UyaArchiveQualificationReport Run(string isoPath)
    {
        using var iso = new FileStream(
            isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.RandomAccess);
        var levels = new List<UyaArchiveLevelQualification>();
        for (var index = 0; index < UyaLevelConstants.LevelInfoCount; index++)
        {
            var entry = UyaLevelInfoReader.ReadEntry(iso, index);
            if (entry.LevelWad.IsEmpty) continue;
            try
            {
                levels.Add(Measure(index, UyaLooseLevelWadExtractor.ExtractPrimary(
                    iso, new UyaLevelInfoSet(index, entry)).Bytes));
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
            {
                levels.Add(Failure(index, exception.Message));
            }
        }

        return new(
            SchemaVersion,
            typeof(LevelArchiveBuilder).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            DateTimeOffset.UtcNow,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            iso.Length,
            levels.Count,
            levels.Count(level => level.Succeeded),
            levels);
    }

    public static UyaArchiveLevelQualification Measure(int levelIndex, byte[] source)
    {
        try
        {
            var sourceInventory = UyaLevelWadInventoryReader.Read(source);
            var containerChecks = sourceInventory.Containers.Select(container =>
            {
                var rebuiltHash = Hash(UyaLevelWadWriter.WriteContainer(container));
                return new UyaArchiveHashCheck(container.Path, "uncompressed-container",
                    container.Sha256, rebuiltHash, container.Sha256 == rebuiltHash);
            }).ToArray();

            using var completed = new ManualResetEventSlim();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var peakWorkingSet = process.WorkingSet64;
            var sampler = new Thread(() =>
            {
                while (!completed.Wait(10))
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                }
            }) { IsBackground = true };
            sampler.Start();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            LevelArchiveBuildResult result;
            try
            {
                result = LevelArchiveBuilder.Build(GameId.UYA, 
                    source,
                    options: new() { RequireSourceEquality = true });
            }
            finally
            {
                completed.Set();
                sampler.Join();
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            }
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (!result.Succeeded || result.OutputBytes is null)
                return Failure(levelIndex, result.Diagnostics.Select(value => value.Message).ToArray(),
                    source.Length, elapsed, allocated, peakWorkingSet);

            var outputInventory = UyaLevelWadInventoryReader.Read(result.OutputBytes);
            var outputContainers = outputInventory.Containers.ToDictionary(value => value.Path, StringComparer.Ordinal);
            var nestedParentSlots = sourceInventory.Containers
                .Where(value => value.SourceContainerPath is not null)
                .Select(value => value.Path == "gameplay/gameplay_core.bin" ? "gameplay/gameplay.bin" : value.Path)
                .ToHashSet(StringComparer.Ordinal);
            var payloadChecks = sourceInventory.Containers.SelectMany(sourceContainer =>
            {
                if (!outputContainers.TryGetValue(sourceContainer.Path, out var outputContainer))
                    return [new UyaArchiveHashCheck(sourceContainer.Path, "final-payload", sourceContainer.Sha256,
                        "missing", false)];
                var outputSlots = outputContainer.Slots.ToDictionary(value => value.Path, StringComparer.Ordinal);
                return sourceContainer.Slots.Where(sourceSlot => !nestedParentSlots.Contains(sourceSlot.Path))
                    .Select(sourceSlot =>
                {
                    if (!outputSlots.TryGetValue(sourceSlot.Path, out var outputSlot))
                        return new(sourceSlot.Path, "final-payload", SemanticHash(sourceSlot), "missing", false);
                    var sourceHash = SemanticHash(sourceSlot);
                    var outputHash = SemanticHash(outputSlot);
                    return new UyaArchiveHashCheck(sourceSlot.Path, "final-payload",
                        sourceHash, outputHash, sourceHash == outputHash);
                });
            }).ToArray();
            var checks = containerChecks.Concat(payloadChecks).ToArray();
            var diagnostics = result.Diagnostics.Select(value => $"{value.Code}: {value.Message}")
                .Concat(checks.Where(check => !check.Matched).Select(check => $"Hash mismatch: {check.Path}"))
                .ToArray();
            var uncompressedBytes = result.Compressions.Sum(value => (long)value.UncompressedSize);
            var compressedBytes = result.Compressions.Sum(value => (long)value.CompressedSize);
            var succeeded = diagnostics.Length == 0 && checks.All(check => check.Matched);

            return new(
                levelIndex,
                succeeded,
                source.Length,
                result.OutputBytes.Length,
                elapsed.TotalMilliseconds,
                elapsed.TotalSeconds == 0 ? 0 : source.Length / elapsed.TotalSeconds / (1024 * 1024),
                allocated,
                peakWorkingSet,
                uncompressedBytes == 0 ? 1 : (double)compressedBytes / uncompressedBytes,
                result.SourceSha256,
                result.UncompressedSha256,
                result.CompressedSha256,
                sourceInventory.Containers.Count,
                result.Compressions.Count,
                sourceInventory.Containers.Count + 1,
                checks,
                diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return Failure(levelIndex, exception.Message, source.Length);
        }
    }

    public static void Write(UyaArchiveQualificationReport report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static UyaArchiveLevelQualification Failure(
        int levelIndex,
        string diagnostic,
        long sourceSize = 0,
        TimeSpan elapsed = default,
        long allocated = 0,
        long peakWorkingSet = 0) =>
        Failure(levelIndex, [diagnostic], sourceSize, elapsed, allocated, peakWorkingSet);

    private static UyaArchiveLevelQualification Failure(
        int levelIndex,
        IReadOnlyList<string> diagnostics,
        long sourceSize = 0,
        TimeSpan elapsed = default,
        long allocated = 0,
        long peakWorkingSet = 0) =>
        new(levelIndex, false, sourceSize, 0, elapsed.TotalMilliseconds, 0, allocated,
            peakWorkingSet == 0 ? Process.GetCurrentProcess().WorkingSet64 : peakWorkingSet,
            0, null, null, null, 0, 0, 0, [], diagnostics);

    private static string SemanticHash(UyaContainerSlot slot) =>
        Hash(slot.Compression == UyaContainerCompression.Wad
            ? WadCompression.Decompress(slot.Bytes.Span)
            : slot.Bytes.Span);

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record UyaArchiveQualificationReport(
    int SchemaVersion,
    string SdkVersion,
    DateTimeOffset GeneratedAtUtc,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    long IsoSize,
    int LevelCount,
    int PassedLevelCount,
    IReadOnlyList<UyaArchiveLevelQualification> Levels);

internal sealed record UyaArchiveLevelQualification(
    int LevelIndex,
    bool Succeeded,
    long SourceSize,
    long OutputSize,
    double ElapsedMilliseconds,
    double ThroughputMiBPerSecond,
    long ManagedAllocatedBytes,
    long PeakWorkingSetBytes,
    double CompressionRatio,
    string? SourceSha256,
    string? UncompressedSha256,
    string? OutputSha256,
    int ContainerCount,
    int CompressionCount,
    int BulkBufferCopyCount,
    IReadOnlyList<UyaArchiveHashCheck> HashChecks,
    IReadOnlyList<string> Diagnostics);

internal sealed record UyaArchiveHashCheck(
    string Path,
    string Kind,
    string SourceSha256,
    string RebuiltSha256,
    bool Matched);
