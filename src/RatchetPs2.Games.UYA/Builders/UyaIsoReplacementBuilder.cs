using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaIsoReplacementBuilder
{
    private const int BufferSize = 1024 * 1024;

    public static async Task BuildAsync(
        Stream source,
        Stream destination,
        IsoPatchPlan plan,
        Func<long, long, ValueTask>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var replacement = Validate(source, destination, plan);
        VerifySourceLevel(source, plan);
        source.Position = 0;
        destination.Position = 0;
        destination.SetLength(0);
        var buffer = System.GC.AllocateUninitializedArray<byte>(BufferSize);
        long copied = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            copied += count;
            if (progress is not null) await progress(copied, replacement.OutputIsoLength);
        }

        destination.SetLength(replacement.OutputIsoLength);
        WriteLevelInfo(destination, plan.LevelIndex, replacement);
        destination.Position = checked((long)replacement.HeaderSector * UyaLevelConstants.SectorSize);
        await destination.WriteAsync(replacement.LevelWadBytes, cancellationToken);
        UpdateVolumeDescriptors(destination, checked((int)(replacement.OutputIsoLength / UyaLevelConstants.SectorSize)));
        await destination.FlushAsync(cancellationToken);
        if (progress is not null)
            await progress(replacement.OutputIsoLength, replacement.OutputIsoLength);
    }

    public static void Verify(Stream iso, IsoPatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(iso);
        ArgumentNullException.ThrowIfNull(plan);
        var replacement = plan.Replacement
            ?? throw new InvalidDataException("The UYA ISO patch plan has no full-image replacement.");
        if (!iso.CanRead || !iso.CanSeek || iso.Length != replacement.OutputIsoLength)
            throw new InvalidDataException("The replacement UYA ISO has an unexpected size or stream shape.");
        var info = UyaLevelInfoReader.ReadLevelSet(iso, plan.LevelIndex).RequestedLevel.LevelWad;
        if (info.Offset != replacement.HeaderSector || info.Length != plan.RequiredSectors)
            throw new InvalidDataException("The replacement UYA ISO level table does not match its plan.");
        VerifyVolumeSize(iso, checked((int)(replacement.OutputIsoLength / UyaLevelConstants.SectorSize)));
        var installed = UyaLooseLevelWadExtractor.ExtractPrimary(iso, plan.LevelIndex).Bytes;
        if (Hash(installed) != plan.OutputLevelWadSha256)
            throw new IOException("The replacement UYA ISO installed level failed verification.");
    }

    private static IsoReplacementPlan Validate(Stream source, Stream destination, IsoPatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        var replacement = plan.Replacement
            ?? throw new InvalidDataException("The UYA ISO patch plan has no full-image replacement.");
        if (plan.SchemaVersion != UyaIsoPatchPlanner.SchemaVersion
            || plan.FitsInPlace
            || plan.Ranges.Count != 0
            || !source.CanRead
            || !source.CanSeek
            || source.Length != plan.IsoLength
            || !destination.CanRead
            || !destination.CanWrite
            || !destination.CanSeek
            || replacement.HeaderSector != plan.IsoLength / UyaLevelConstants.SectorSize
            || replacement.PayloadBaseSector != replacement.HeaderSector
            || replacement.LevelWadBytes.Length != (long)plan.RequiredSectors * UyaLevelConstants.SectorSize
            || replacement.RequiredFreeBytes != replacement.OutputIsoLength
            || replacement.OutputIsoLength != plan.IsoLength + replacement.LevelWadBytes.Length)
            throw new InvalidDataException("The UYA full-image replacement plan is invalid.");
        return replacement;
    }

    private static void VerifySourceLevel(Stream source, IsoPatchPlan plan)
    {
        var level = UyaLooseLevelWadExtractor.ExtractPrimary(source, plan.LevelIndex).Bytes;
        if (Hash(level) != plan.SourceLevelWadSha256)
            throw new InvalidDataException("The UYA ISO level changed after replacement planning.");
    }

    private static void WriteLevelInfo(Stream destination, int levelIndex, IsoReplacementPlan replacement)
    {
        destination.Position = checked(UyaLevelConstants.RetailLevelInfoTableOffset
            + (levelIndex * UyaLevelConstants.LevelInfoSize) + 8);
        Span<byte> entry = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(entry, replacement.HeaderSector);
        BinaryPrimitives.WriteInt32LittleEndian(
            entry[4..], replacement.LevelWadBytes.Length / UyaLevelConstants.SectorSize);
        destination.Write(entry);
    }

    private static void UpdateVolumeDescriptors(Stream destination, int sectorCount)
    {
        Span<byte> descriptor = stackalloc byte[UyaLevelConstants.SectorSize];
        for (var sector = 16; sector < 32; sector++)
        {
            destination.Position = (long)sector * UyaLevelConstants.SectorSize;
            destination.ReadExactly(descriptor);
            if (!descriptor[1..6].SequenceEqual("CD001"u8)) break;
            if (descriptor[0] is 1 or 2)
            {
                BinaryPrimitives.WriteInt32LittleEndian(descriptor[80..], sectorCount);
                BinaryPrimitives.WriteInt32BigEndian(descriptor[84..], sectorCount);
                destination.Position = (long)sector * UyaLevelConstants.SectorSize;
                destination.Write(descriptor);
            }
            if (descriptor[0] == byte.MaxValue) break;
        }
    }

    private static void VerifyVolumeSize(Stream iso, int sectorCount)
    {
        Span<byte> descriptor = stackalloc byte[UyaLevelConstants.SectorSize];
        iso.Position = 16L * UyaLevelConstants.SectorSize;
        iso.ReadExactly(descriptor);
        if (descriptor[0] == 1 && descriptor[1..6].SequenceEqual("CD001"u8)
            && (BinaryPrimitives.ReadInt32LittleEndian(descriptor[80..]) != sectorCount
                || BinaryPrimitives.ReadInt32BigEndian(descriptor[84..]) != sectorCount))
            throw new IOException("The replacement UYA ISO volume size failed verification.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
