using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class UyaCollisionQualification
{
    public const int SchemaVersion = 2;

    public static UyaCollisionQualificationReport Run(string isoPath)
    {
        using var iso = new FileStream(
            isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.RandomAccess);
        var levels = new List<UyaCollisionLevelQualification>();
        for (var index = 0; index < UyaLevelConstants.LevelInfoCount; index++)
        {
            var entry = UyaLevelInfoReader.ReadEntry(iso, index);
            if (entry.LevelWad.IsEmpty) continue;
            try
            {
                levels.Add(Measure(index, UyaLooseLevelWadExtractor.ExtractPrimary(
                    iso, new UyaLevelInfoSet(index, entry)).Bytes));
            }
            catch (Exception exception) when (exception is InvalidDataException
                or ArgumentException
                or OverflowException)
            {
                levels.Add(new(index, false, 0, 0, 0, 0, 0, [], [], [exception.Message]));
            }
        }

        return new(
            SchemaVersion,
            typeof(CollisionConverter).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            DateTimeOffset.UtcNow,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            iso.Length,
            levels.Count,
            levels.Count(level => level.Succeeded),
            levels);
    }

    public static UyaCollisionLevelQualification Measure(int levelIndex, byte[] levelWad)
    {
        var package = UyaLevelWadUnpacker.Unpack(levelWad);
        var payloads = new List<(string Name, byte[] Bytes)> { ("primary", ReadPrimaryCollision(package)) };
        for (var index = 1; index < package.LevelWad.Chunks.Count; index++)
        {
            var chunk = package.Files.SingleOrDefault(file => file.Path == $"level_wad/chunks/chunk{index}.wad");
            if (chunk is not null)
                payloads.Add(($"chunk-{index}", TfragChunkWadReader.ReadCollisionPayload(chunk.Bytes)));
        }

        var diagnostics = new List<string>();
        var solidPieces = 0;
        var barriers = 0;
        var faces = 0;
        var nonemptyPayloads = 0;
        var hashes = new List<UyaCollisionHashCheck>();
        var octantSummaries = new List<UyaCollisionOctantSummary>();
        foreach (var (name, payload) in payloads.Where(value => value.Bytes.Length > 0))
        {
            nonemptyPayloads++;
            var inspection = CollisionConverter.Inspect(payload, GameId.UYA);
            solidPieces += inspection.Pieces.Count(piece => piece.Kind == CollisionPieceKind.Solid);
            barriers += inspection.Pieces.Count(piece => piece.Kind == CollisionPieceKind.PlayerBarrier);
            faces += inspection.Pieces.Sum(piece => piece.FaceCount);
            var analysis = CollisionConverter.Analyze(payload, GameId.UYA);
            octantSummaries.Add(new(
                name,
                analysis.OccupiedOctantCount,
                analysis.DuplicateFaceCount,
                analysis.Octants.Count == 0 ? 0 : analysis.Octants.Max(octant => octant.FaceCount),
                analysis.Octants.Count == 0 ? 0 : analysis.Octants.Max(octant => octant.VertexCount),
                analysis.Octants.Count == 0 ? 0 : analysis.Octants.Max(octant => octant.QuadCount),
                analysis.Octants.Count == 0 ? 0 : analysis.Octants.Max(octant => octant.EncodedByteCount),
                analysis.HardViolationCount));
            var composition = CollisionConverter.Compose(payload, GameId.UYA, []);
            var sourceHash = Hash(payload);
            var outputHash = Hash(composition.Bytes);
            var matched = !composition.Changed
                && composition.Bytes.AsSpan().SequenceEqual(payload)
                && sourceHash.Equals(outputHash, StringComparison.Ordinal);
            hashes.Add(new(name, payload.Length, sourceHash, outputHash, matched));
            if (!matched) diagnostics.Add($"Collision payload {name} changed during no-edit composition.");
        }

        return new(levelIndex, diagnostics.Count == 0, nonemptyPayloads, solidPieces, barriers, faces,
            payloads.Sum(value => (long)value.Bytes.Length), hashes, octantSummaries, diagnostics);
    }

    public static void Write(UyaCollisionQualificationReport report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] ReadPrimaryCollision(UyaLevelWadPackage package)
    {
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(package.Files);
        var header = LevelAssetReader.ReadHeader(source.HeaderBytes);
        var assets = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes)
            : source.AssetWadBytes;
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(
            header,
            assets.Length,
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.MobyModelOffset, header.MobyModelCount),
            LevelAssetReader.ReadModelDefinitions(source.HeaderBytes, header.TieModelOffset, header.TieModelCount),
            LevelAssetReader.ReadShrubDefinitions(source.HeaderBytes, header.ShrubModelOffset, header.ShrubModelCount),
            [header.SceneViewSize]);
        return LevelAssetReader.ReadAssetSlice(assets, header.CollisionOffset, offsets);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record UyaCollisionQualificationReport(
    int SchemaVersion,
    string SdkVersion,
    DateTimeOffset GeneratedAtUtc,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    long IsoSize,
    int LevelCount,
    int PassedLevelCount,
    IReadOnlyList<UyaCollisionLevelQualification> Levels);

internal sealed record UyaCollisionLevelQualification(
    int LevelIndex,
    bool Succeeded,
    int PayloadCount,
    int SolidPieceCount,
    int PlayerBarrierCount,
    int FaceCount,
    long SourceBytes,
    IReadOnlyList<UyaCollisionHashCheck> HashChecks,
    IReadOnlyList<UyaCollisionOctantSummary> OctantSummaries,
    IReadOnlyList<string> Diagnostics);

internal sealed record UyaCollisionOctantSummary(
    string Payload,
    int OccupiedOctantCount,
    int DuplicateFaceCount,
    int MaximumFaceCount,
    int MaximumVertexCount,
    int MaximumQuadCount,
    int MaximumEncodedByteCount,
    int HardViolationCount);

internal sealed record UyaCollisionHashCheck(
    string Payload,
    int SourceBytes,
    string SourceSha256,
    string OutputSha256,
    bool Matched);
