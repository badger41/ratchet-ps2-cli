using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Wad;

public static class WadTocReader
{
    private static ReadOnlySpan<byte> WadMagic => "WAD"u8;

    public static WadTocArchive Read(Stream stream, long startOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be seekable.", nameof(stream));
        }

        if (startOffset < 0 || startOffset > stream.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        stream.Position = startOffset;

        var tocSizeBytes = stream.ReadInt32LittleEndian();
        if (tocSizeBytes < 8)
        {
            throw new InvalidDataException($"Invalid TOC size '{tocSizeBytes}'.");
        }

        if ((tocSizeBytes - 8) % 8 != 0)
        {
            throw new InvalidDataException($"Invalid TOC size '{tocSizeBytes}' for 8-byte entries.");
        }

        if (startOffset + tocSizeBytes > stream.Length)
        {
            throw new InvalidDataException("TOC size exceeds stream length.");
        }

        var tocIndex = stream.ReadInt32LittleEndian();
        if (tocIndex < 0)
        {
            throw new InvalidDataException($"Invalid TOC index '{tocIndex}'.");
        }

        var entryCount = (tocSizeBytes - 8) / 8;
        var dataStartOffset = checked(startOffset + ((long)tocIndex * Sector32.SizeInBytes));
        var entries = new List<WadTocEntry>(entryCount);

        for (var index = 0; index < entryCount; index++)
        {
            var offsetSectors = stream.ReadInt32LittleEndian();
            var sizeSectors = stream.ReadInt32LittleEndian();
            var range = new SectorRange(new Sector32(offsetSectors), new Sector32(sizeSectors));

            if (range.IsEmpty)
            {
                continue;
            }

            if (offsetSectors < 0 || sizeSectors < 0)
            {
                throw new InvalidDataException($"TOC entry {index} contains a negative sector offset or size.");
            }

            var absoluteOffset = checked(dataStartOffset + range.OffsetBytes);
            if (absoluteOffset < startOffset || absoluteOffset >= stream.Length)
            {
                throw new InvalidDataException($"TOC entry {index} points outside the stream bounds.");
            }

            var remainingBytes = stream.Length - absoluteOffset;
            var sizeBytes = Math.Min(range.SizeBytes, remainingBytes);
            if (sizeBytes <= 0)
            {
                continue;
            }

            entries.Add(new WadTocEntry(index, range, sizeBytes));
        }

        return new WadTocArchive(tocSizeBytes, tocIndex, dataStartOffset, entries);
    }

    public static byte[] ReadEntryBytes(Stream stream, WadTocArchive archive, WadTocEntry entry)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(entry);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be readable and seekable.", nameof(stream));
        }

        if (entry.SizeBytes > int.MaxValue)
        {
            throw new InvalidDataException("TOC entry is too large to materialize into a single byte array.");
        }

        var absoluteOffset = archive.DataStartOffset + entry.OffsetBytes;
        var buffer = new byte[(int)entry.SizeBytes];
        stream.Position = absoluteOffset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    public static byte[] ReadEntryBytesTrimmed(Stream stream, WadTocArchive archive, WadTocEntry entry)
    {
        var buffer = ReadEntryBytes(stream, archive, entry);
        var trimmedLength = buffer.Length;

        while (trimmedLength > 0 && buffer[trimmedLength - 1] == 0)
        {
            trimmedLength--;
        }

        return trimmedLength == buffer.Length
            ? buffer
            : buffer[..trimmedLength];
    }

    public static bool IsWad(ReadOnlySpan<byte> data)
    {
        return data.Length >= WadMagic.Length && data[..WadMagic.Length].SequenceEqual(WadMagic);
    }
}