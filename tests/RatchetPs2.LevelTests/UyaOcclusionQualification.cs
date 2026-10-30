using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class UyaOcclusionQualification
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string isoPath, string reportPath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(isoPath);
        var output = Path.GetFullPath(reportPath);
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || output.Equals(source, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Occlusion qualification output must be a separate .json report.");
        using var iso = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        var profile = DiscImageInspector.GetSupportedProfile(GameId.UYA);
        var metadata = DiscImageInspector.Inspect(GameId.UYA, iso);
        if (metadata.Game != profile.Game || metadata.Size != profile.Size || metadata.Serial != profile.Serial
            || metadata.Region != profile.Region || metadata.Revision != profile.Revision)
            throw new InvalidDataException("Occlusion qualification requires the supported clean UYA NTSC-U 1.00 image.");
        Console.Error.WriteLine("Verifying complete source image identity...");
        iso.Position = 0;
        var md5 = Convert.ToHexString(await MD5.HashDataAsync(iso, cancellationToken)).ToLowerInvariant();
        if (md5 != profile.Md5) throw new InvalidDataException("Source image MD5 does not match the supported clean UYA image.");
        var reports = new List<UyaOcclusionCorpusLevel>();
        foreach (var level in LevelCatalogReader.FindAvailable(GameId.UYA, iso))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Error.WriteLine($"Analyzing UYA level {level}...");
            try
            {
                var info = UyaLevelInfoReader.ReadLevelSet(iso, level);
                var header = UyaLevelWadReader.ReadLevelWad(UyaLevelInfoReader.ReadSectorHeader(
                    iso, info.RequestedLevel.LevelWad, UyaLevelConstants.LevelWadHeaderSectorCount));
                var wadLength = checked((long)UyaLooseLevelWadExtractor.CalculatePrimarySectorCount(header) * UyaLevelConstants.SectorSize);
                if (wadLength > UyaOcclusionLevelAnalyzer.MaximumLevelWadBytes)
                    throw new InvalidDataException("Level WAD exceeds the analysis allocation limit.");
                var bytes = UyaLooseLevelWadExtractor.ExtractPrimary(iso, info).Bytes;
                var result = UyaOcclusionLevelAnalyzer.Analyze(bytes, cancellationToken);
                if (result.Grid is { NoEditMappingWriterByteIdentity: false })
                    throw new InvalidDataException("No-edit mapping writer changed source bytes.");
                if (result.ExternalMappingPrefixEqualsGameplay is false)
                    throw new InvalidDataException("Separate native mapping tables disagree in their declared records.");
                var links = result.Sources;
                var summary = links is null ? null : new UyaOcclusionSourceSummary(
                    links.Links.Count, links.UnresolvedMappings.Count, links.UnmappedSources.Count, links.AmbiguousSources.Count,
                    links.PrimaryTfragGroups, links.ChunkTfragGroups, links.TfragTriangleCountMatches, links.TfragTriangleCountMismatches,
                    links.TfragStashGroups, links.MobyFlagZeroInstances,
                    Hash(JsonSerializer.SerializeToUtf8Bytes(links)),
                    links.UnresolvedMappings.Take(16).ToArray(), links.UnmappedSources.Take(16).ToArray(), links.AmbiguousSources.Take(16).ToArray());
                reports.Add(new(level, result.Status, result.Grid, summary, result.ExternalMappingBytes,
                    result.ExternalMappingSha256, result.ExternalMappingPrefixEqualsGameplay,
                    [result.OcclusionOctreeOffset, result.OcclusionRadiusOffset, result.OcclusionRadius2Offset], result.ChunkTerrainSources));
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException or KeyNotFoundException)
            {
                throw new InvalidDataException($"UYA level {level} occlusion analysis failed: {exception.Message}", exception);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = typeof(UyaOcclusionAnalyzer).Assembly;
        var report = new UyaOcclusionCorpusReport(2, md5, iso.Length,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            Hash(await File.ReadAllBytesAsync(assembly.Location, cancellationToken)), RuntimeInformation.FrameworkDescription, reports);
        Write(report, output, cancellationToken);
        Console.WriteLine($"Qualified {reports.Count} UYA entries, {reports.Count(level => level.Grid is not null)} grids, "
            + $"{reports.Sum(level => level.Grid?.Octants ?? 0)} octants.");
    }

    internal static void Write(UyaOcclusionCorpusReport report, string output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (report.SchemaVersion != 2) throw new InvalidDataException("Unexpected occlusion report schema.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        using var validation = JsonDocument.Parse(bytes);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".occlusion-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record UyaOcclusionCorpusReport(
    int SchemaVersion, string SourceMd5, long SourceBytes, string SdkVersion,
    string SdkAssemblySha256, string Runtime, IReadOnlyList<UyaOcclusionCorpusLevel> Levels);

internal sealed record UyaOcclusionCorpusLevel(
    int Level, string Status, UyaOcclusionAnalysis? Grid, UyaOcclusionSourceSummary? Sources,
    int ExternalMappingBytes, string ExternalMappingSha256, bool? ExternalMappingPrefixEqualsGameplay,
    int[] AuxiliaryOffsets, IReadOnlyList<int> ChunkTerrainSources);

internal sealed record UyaOcclusionSourceSummary(
    int Links, int UnresolvedMappings, int UnmappedSources, int AmbiguousSources,
    int PrimaryTfragGroups, int ChunkTfragGroups, int TfragTriangleCountMatches, int TfragTriangleCountMismatches,
    int TfragStashGroups, int MobyFlagZeroInstances, string LinksSha256,
    IReadOnlyList<string> UnresolvedMappingExamples, IReadOnlyList<string> UnmappedSourceExamples,
    IReadOnlyList<string> AmbiguousSourceExamples);
