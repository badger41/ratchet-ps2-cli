using System.Runtime.InteropServices;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;
using RatchetPs2.Games.UYA.Collision;

internal static class UyaTieCollisionQualification
{
    public const int SchemaVersion = 5;

    public static UyaTieCollisionQualificationReport Run(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var ties = Directory.EnumerateFiles(root, "tie.bin", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (
                Path: Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                Tie: TieClassReader.Read(
                    File.ReadAllBytes(path),
                    TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)))))
            .ToArray();
        var lodCount = ties.Sum(value => value.Tie.LodTopologies.Count(lod => lod.TriangleCount != 0));
        var hull = MeasureHull(ties);
        return new(
            SchemaVersion,
            typeof(UyaTieCollisionHullGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            DateTimeOffset.UtcNow,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            ties.Length,
            lodCount,
            hull.FailedCandidateCount == 0 && hull.CandidatesWithHardViolations == 0,
            hull);
    }

    public static void Write(UyaTieCollisionQualificationReport report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static UyaTieCollisionHullQualification MeasureHull(
        IReadOnlyList<(string Path, TieClass Tie)> ties)
    {
        var failures = new List<UyaTieCollisionCandidateFailure>();
        var succeeded = 0;
        long totalFaces = 0;
        long totalVertices = 0;
        var maximumFaces = 0;
        var maximumVertices = 0;
        var maximumOctantFaces = 0;
        var maximumOctantBytes = 0;
        var candidatesWithHardViolations = 0;
        var maximumSourceVertexDeviation = 0f;
        foreach (var (path, tie) in ties)
        foreach (var lod in tie.LodTopologies.Where(value => value.TriangleCount != 0))
        {
            try
            {
                var candidate = UyaTieCollisionHullGenerator.Generate(
                    tie,
                    $"qualification:Hull:{path}:{lod.LodIndex}",
                    lod.LodIndex,
                    profileSections: 1);
                succeeded++;
                totalFaces += candidate.GeneratedFaceCount;
                totalVertices += candidate.GeneratedVertexCount;
                maximumFaces = Math.Max(maximumFaces, candidate.GeneratedFaceCount);
                maximumVertices = Math.Max(maximumVertices, candidate.GeneratedVertexCount);
                maximumOctantFaces = Math.Max(maximumOctantFaces,
                    candidate.Analysis.Octants.Count == 0
                        ? 0
                        : candidate.Analysis.Octants.Max(value => value.FaceCount));
                maximumOctantBytes = Math.Max(maximumOctantBytes,
                    candidate.Analysis.Octants.Count == 0
                        ? 0
                        : candidate.Analysis.Octants.Max(value => value.EncodedByteCount));
                if (candidate.Analysis.HardViolationCount != 0) candidatesWithHardViolations++;
                maximumSourceVertexDeviation = MathF.Max(
                    maximumSourceVertexDeviation,
                    candidate.MaximumSourceVertexDeviation);
            }
            catch (Exception exception) when (exception is InvalidDataException
                or ArgumentException
                or OverflowException)
            {
                failures.Add(new(path, lod.LodIndex, exception.Message));
            }
        }

        return new(
            succeeded + failures.Count,
            succeeded,
            failures.Count,
            totalFaces,
            totalVertices,
            maximumFaces,
            maximumVertices,
            maximumOctantFaces,
            maximumOctantBytes,
            candidatesWithHardViolations,
            maximumSourceVertexDeviation,
            failures);
    }
}

internal sealed record UyaTieCollisionQualificationReport(
    int SchemaVersion,
    string SdkVersion,
    DateTimeOffset GeneratedAtUtc,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int TieCount,
    int LodCount,
    bool Succeeded,
    UyaTieCollisionHullQualification Hull);

internal sealed record UyaTieCollisionCandidateFailure(
    string TiePath,
    int LodIndex,
    string Diagnostic);

internal sealed record UyaTieCollisionHullQualification(
    int CandidateCount,
    int SucceededCandidateCount,
    int FailedCandidateCount,
    long TotalGeneratedFaces,
    long TotalGeneratedVertices,
    int MaximumGeneratedFaces,
    int MaximumGeneratedVertices,
    int MaximumOctantFaces,
    int MaximumOctantEncodedBytes,
    int CandidatesWithHardViolations,
    float MaximumSourceVertexDeviation,
    IReadOnlyList<UyaTieCollisionCandidateFailure> Failures);
