using System.Security.Cryptography;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaIsoPatchApplier
{
    public static void ValidateSource(
        Stream iso,
        IsoPatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(iso, plan);
        foreach (var range in plan.Ranges)
            if (HashRange(iso, range.Offset, range.Length, cancellationToken) != range.SourceSha256)
                throw new InvalidDataException($"UYA ISO patch preimage {range.Name} no longer matches its plan.");
    }

    public static void ApplyRange(
        Stream iso,
        IsoPatchPlan plan,
        int rangeIndex,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(iso, plan);
        if ((uint)rangeIndex >= (uint)plan.Ranges.Count) throw new ArgumentOutOfRangeException(nameof(rangeIndex));
        var range = plan.Ranges[rangeIndex];
        if (HashRange(iso, range.Offset, range.Length, cancellationToken) != range.SourceSha256)
            throw new InvalidDataException($"UYA ISO patch preimage {range.Name} no longer matches its plan.");
        iso.Position = range.Offset;
        iso.Write(range.OutputBytes.Span);
        iso.Flush();
        VerifyRange(iso, range, cancellationToken);
    }

    public static void VerifyOutput(
        Stream iso,
        IsoPatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(iso, plan);
        foreach (var range in plan.Ranges) VerifyRange(iso, range, cancellationToken);
    }

    public static void VerifyInstalledLevel(
        Stream iso,
        IsoPatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = UyaLooseLevelWadExtractor.ExtractPrimary(iso, plan.LevelIndex).Bytes;
        var hash = Convert.ToHexString(SHA256.HashData(installed)).ToLowerInvariant();
        if (hash != plan.OutputLevelWadSha256)
            throw new IOException("The installed UYA level failed final verification.");
    }

    public static void VerifyRangeOutput(
        Stream iso,
        IsoPatchPlan plan,
        int rangeIndex,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(iso, plan);
        if ((uint)rangeIndex >= (uint)plan.Ranges.Count) throw new ArgumentOutOfRangeException(nameof(rangeIndex));
        VerifyRange(iso, plan.Ranges[rangeIndex], cancellationToken);
    }

    public static string HashRange(
        Stream stream,
        long offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || length < 0 || length > stream.Length || offset > stream.Length - length)
            throw new InvalidDataException("The UYA ISO verification range exceeds the stream.");
        stream.Position = offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[Math.Min(64 * 1024, Math.Max(1, length))];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException("The UYA ISO ended inside a verification range.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void VerifyRange(
        Stream iso,
        IsoPatchRange range,
        CancellationToken cancellationToken)
    {
        if (HashRange(iso, range.Offset, range.Length, cancellationToken) != range.OutputSha256)
            throw new IOException($"UYA ISO patch result {range.Name} failed verification.");
    }

    private static void ValidatePlan(Stream iso, IsoPatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(iso);
        ArgumentNullException.ThrowIfNull(plan);
        if (!iso.CanRead || !iso.CanWrite || !iso.CanSeek)
            throw new ArgumentException("The UYA ISO patch stream must be readable, writable, and seekable.", nameof(iso));
        if (plan.SchemaVersion != UyaIsoPatchPlanner.SchemaVersion
            || !plan.FitsInPlace
            || plan.IsoLength != iso.Length
            || plan.Ranges.Count == 0)
            throw new InvalidDataException("The UYA ISO patch plan is not valid for in-place application.");

        long end = 0;
        foreach (var range in plan.Ranges.OrderBy(value => value.Offset))
        {
            if (string.IsNullOrWhiteSpace(range.Name)
                || range.Alignment <= 0
                || range.Offset < end
                || range.Offset % range.Alignment != 0
                || range.Length <= 0
                || range.Length != range.OutputBytes.Length
                || range.Length > iso.Length
                || range.Offset > iso.Length - range.Length
                || range.SourceSha256.Length != 64
                || range.OutputSha256.Length != 64
                || Convert.ToHexString(SHA256.HashData(range.OutputBytes.Span)).ToLowerInvariant()
                    != range.OutputSha256)
                throw new InvalidDataException("The UYA ISO patch plan contains an invalid range.");
            end = range.Offset + range.Length;
        }
    }
}
