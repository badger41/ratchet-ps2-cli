using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Wad;

public static class WadReader
{
    public static WadArchive Read(Stream stream)
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

        stream.Position = 0;
        var headerSize = stream.ReadInt32LittleEndian();

        if (headerSize < 8 || headerSize % 8 != 0)
        {
            throw new InvalidDataException($"Invalid WAD header size '{headerSize}'.");
        }

        if (headerSize > stream.Length)
        {
            throw new InvalidDataException("WAD header size exceeds stream length.");
        }

        var entryCount = headerSize / 8;
        var entries = new List<WadEntry>(entryCount);

        stream.Position = 8;

        for (var index = 0; index < entryCount; index++)
        {
            var offsetSectors = stream.ReadInt32LittleEndian();
            var sizeSectors = stream.ReadInt32LittleEndian();
            var range = new SectorRange(new Sector32(offsetSectors), new Sector32(sizeSectors));

            if (range.IsEmpty)
            {
                continue;
            }

            if (range.OffsetBytes < 0 || range.SizeBytes < 0 || range.OffsetBytes + range.SizeBytes > stream.Length)
            {
                throw new InvalidDataException($"WAD entry {index} points outside the stream bounds.");
            }

            entries.Add(new WadEntry(index, range));
        }

        return new WadArchive(headerSize, entries);
    }

    public static byte[] ReadEntryBytes(Stream stream, WadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entry);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be readable and seekable.", nameof(stream));
        }

        if (entry.SizeBytes > int.MaxValue)
        {
            throw new InvalidDataException("WAD entry is too large to materialize into a single byte array.");
        }

        var buffer = new byte[(int)entry.SizeBytes];
        stream.Position = entry.OffsetBytes;
        stream.ReadExactly(buffer);
        return buffer;
    }
}