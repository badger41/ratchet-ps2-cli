using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaIsoPatchPlanner
{
    public const int SchemaVersion = 1;

    public static IsoPatchPlan Create(
        Stream iso,
        int levelIndex,
        ReadOnlySpan<byte> outputLevelWad,
        bool forceFullImage = false,
        CancellationToken cancellationToken = default) => Create(
            iso,
            UyaLevelInfoReader.ReadLevelSet(iso, levelIndex),
            outputLevelWad,
            forceFullImage,
            cancellationToken);

    public static IsoPatchPlan Create(
        Stream iso,
        UyaLevelInfoSet layout,
        ReadOnlySpan<byte> outputLevelWad,
        bool forceFullImage = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(iso);
        ArgumentNullException.ThrowIfNull(layout);
        if (!iso.CanRead || !iso.CanSeek)
            throw new ArgumentException("The development ISO stream must be readable and seekable.", nameof(iso));
        if (iso.Length <= 0 || iso.Length % UyaLevelConstants.SectorSize != 0)
            throw new InvalidDataException("The development ISO length is not sector aligned.");
        if (outputLevelWad.Length < UyaLevelConstants.SectorSize
            || outputLevelWad.Length % UyaLevelConstants.SectorSize != 0)
            throw new InvalidDataException("The packed UYA level WAD must contain whole sectors.");

        var levelIndex = layout.RequestedLevelIndex;
        var currentLevelInfo = UyaLevelInfoReader.ReadLevelSet(iso, levelIndex);
        var source = UyaLooseLevelWadExtractor.ExtractPrimary(iso, layout);
        var normalizedOutput = outputLevelWad.ToArray();
        var outputHeader = UyaLevelWadReader.ReadLevelWad(normalizedOutput);
        if (outputHeader.Level != levelIndex)
            throw new InvalidDataException(
                $"Packed UYA level {outputHeader.Level} cannot replace level {levelIndex}.");
        BinaryPrimitives.WriteInt32LittleEndian(
            normalizedOutput.AsSpan(sizeof(int)), source.PayloadBaseSector);
        outputHeader = UyaLevelWadReader.ReadLevelWad(normalizedOutput);
        if (UyaLooseLevelWadExtractor.CalculatePrimarySectorCount(outputHeader)
            != normalizedOutput.Length / UyaLevelConstants.SectorSize)
            throw new InvalidDataException("Packed UYA level length does not match its referenced payload ranges.");

        var capacity = layout.RequestedLevel.LevelWad.Length;
        var required = normalizedOutput.Length / UyaLevelConstants.SectorSize;
        var fits = !forceFullImage && capacity > 0 && required <= capacity;
        var ranges = fits
            ? BuildRanges(iso, source, currentLevelInfo, normalizedOutput, cancellationToken)
            : [];
        var replacement = fits ? null : BuildReplacement(iso, normalizedOutput);
        var outputHash = replacement is null
            ? Hash(normalizedOutput)
            : Hash(replacement.LevelWadBytes.Span);
        return new(
            SchemaVersion,
            levelIndex,
            iso.Length,
            source.HeaderSector,
            source.PayloadBaseSector,
            capacity,
            required,
            fits,
            Hash(source.Bytes),
            outputHash,
            ranges,
            fits
                ? currentLevelInfo.RequestedLevel.LevelWad == layout.RequestedLevel.LevelWad
                    ? "The level fits its existing ISO allocation; journaled in-place patching is available."
                    : "The level fits its supplied ISO allocation; journaled in-place patching will restore that layout."
                : forceFullImage
                    ? "Full-image replacement was explicitly requested."
                    : $"The level requires {required} sectors but its ISO allocation has {capacity}; full-image replacement is required.",
            replacement);
    }

    public static IsoPatchPlan Create(
        Stream iso,
        IsoLevelAllocation allocation,
        ReadOnlySpan<byte> outputLevelWad,
        bool forceFullImage = false,
        CancellationToken cancellationToken = default) => Create(
            iso,
            new UyaLevelInfoSet(
                allocation.LevelIndex,
                new(allocation.LevelIndex, default, new(allocation.HeaderSector, allocation.CapacitySectors), default)),
            outputLevelWad,
            forceFullImage,
            cancellationToken);

    private static IsoReplacementPlan BuildReplacement(Stream iso, ReadOnlySpan<byte> output)
    {
        var headerSector = checked((int)(iso.Length / UyaLevelConstants.SectorSize));
        var relocated = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(relocated.AsSpan(sizeof(int)), headerSector);
        return new(
            headerSector,
            headerSector,
            checked(iso.Length + relocated.Length),
            checked(iso.Length + relocated.Length),
            relocated);
    }

    private static IReadOnlyList<IsoPatchRange> BuildRanges(
        Stream iso,
        UyaLooseLevelWad source,
        UyaLevelInfoSet currentLevelInfo,
        ReadOnlySpan<byte> output,
        CancellationToken cancellationToken)
    {
        var sectorSize = UyaLevelConstants.SectorSize;
        var header = output[..sectorSize].ToArray();
        var payload = output[sectorSize..].ToArray();
        var ranges = new List<IsoPatchRange>
        {
            Range("level-header", checked((long)source.HeaderSector * sectorSize), header, iso, cancellationToken),
            Range("level-payload", checked(((long)source.PayloadBaseSector + 1) * sectorSize), payload, iso, cancellationToken),
        };
        if (currentLevelInfo.RequestedLevel.LevelWad != source.LevelInfo.RequestedLevel.LevelWad)
        {
            var levelInfo = new byte[sizeof(int) * 2];
            BinaryPrimitives.WriteInt32LittleEndian(levelInfo, source.LevelInfo.RequestedLevel.LevelWad.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(
                levelInfo.AsSpan(sizeof(int)), source.LevelInfo.RequestedLevel.LevelWad.Length);
            ranges.Add(Range(
                "level-info",
                checked(UyaLevelConstants.RetailLevelInfoTableOffset
                    + (source.LevelIndex * UyaLevelConstants.LevelInfoSize) + 8L),
                levelInfo,
                iso,
                cancellationToken,
                sizeof(int)));
        }
        ranges.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        if (ranges.Any(value => value.Offset < 0
            || value.Offset % value.Alignment != 0
            || value.Length > iso.Length
            || value.Offset > iso.Length - value.Length))
            throw new InvalidDataException("The UYA level patch ranges exceed the development ISO.");
        return ranges.Where(value => value.Length > 0).ToArray();
    }

    private static IsoPatchRange Range(
        string name,
        long offset,
        byte[] output,
        Stream iso,
        CancellationToken cancellationToken,
        int alignment = UyaLevelConstants.SectorSize) => new(
            name,
            offset,
            output.Length,
            alignment,
            HashRange(iso, offset, output.Length, cancellationToken),
            Hash(output),
            output);

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashRange(
        Stream stream,
        long offset,
        int length,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || length < 0 || length > stream.Length || offset > stream.Length - length)
            throw new InvalidDataException("The UYA patch preimage range exceeds the development ISO.");
        stream.Position = offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[Math.Min(64 * 1024, Math.Max(1, length))];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException("The development ISO ended inside a patch preimage range.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
