using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Wad.Models;

public enum LevelArchivePhase
{
    Reading,
    Inventory,
    Rebuild,
    Validate,
    Compress,
    Complete,
    Failed,
}

public sealed record LevelArchiveProgress(
    LevelArchivePhase Phase,
    double Completion,
    string Message);

public sealed record LevelArchiveBuildOptions
{
    public bool RequireSourceEquality { get; init; }

    public WadDecompressionOptions Decompression { get; init; } = new();
}

public sealed record LevelArchiveCapability(
    string Game,
    string Region,
    IReadOnlyList<string> Revisions,
    string BakeProfile);

public sealed record LevelArchiveDiagnostic(
    string Code,
    string Message,
    bool Blocking);

public sealed record LevelArchiveChange(
    string ContainerPath,
    string Path,
    int SourceOffset,
    int SourceLength,
    string SourceSha256,
    int OutputOffset,
    int OutputLength,
    string OutputSha256);

public sealed record LevelArchiveCompression(
    string Path,
    int UncompressedSize,
    int CompressedSize,
    string UncompressedSha256,
    string CompressedSha256);

public sealed record LevelArchiveBuildResult(
    bool Succeeded,
    int SchemaVersion,
    string SdkVersion,
    byte[]? OutputBytes,
    int SourceSize,
    string SourceSha256,
    int? UncompressedSize,
    string? UncompressedSha256,
    int? CompressedSize,
    string? CompressedSha256,
    IReadOnlyList<LevelArchiveChange> ChangedRegions,
    IReadOnlyList<LevelArchiveCompression> Compressions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LevelArchiveDiagnostic> Diagnostics);
