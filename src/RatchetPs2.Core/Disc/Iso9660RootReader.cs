using System.Buffers.Binary;
using System.Text;

namespace RatchetPs2.Core.Disc;

public static class Iso9660RootReader
{
    public static byte[] ReadFile(Stream source, string fileName, int maximumLength = 32 * 1024 * 1024)
    {
        if (maximumLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var entry = FindFile(source, fileName);
        return ReadRange(source, entry.Sector, entry.Length, maximumLength);
    }

    public static Iso9660RootFile FindFile(Stream source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The ISO stream must be readable and seekable.", nameof(source));
        var descriptor = ReadRange(source, 16, 2048, 2048);
        if (descriptor[0] != 1 || !descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8))
            throw new InvalidDataException("Missing ISO 9660 primary volume descriptor.");
        var root = descriptor.AsSpan(156);
        var directory = ReadRange(source, UInt32(root, 2), UInt32(root, 10), 16 * 1024 * 1024);
        for (var offset = 0; offset < directory.Length;)
        {
            var length = directory[offset];
            if (length == 0)
            {
                offset = (offset / 2048 + 1) * 2048;
                continue;
            }
            if (length < 34 || offset + length > directory.Length || offset % 2048 + length > 2048)
                throw new InvalidDataException("Invalid ISO directory record.");
            var record = directory.AsSpan(offset, length);
            if (33 + record[32] > length) throw new InvalidDataException("Invalid ISO file name.");
            var name = Encoding.ASCII.GetString(record.Slice(33, record[32])).Split(';')[0];
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                if ((record[25] & 0x82) != 0)
                    throw new InvalidDataException("Expected a single-extent regular ISO file.");
                return new((long)UInt32(root, 2) * 2048 + offset, UInt32(record, 2), UInt32(record, 10));
            }
            offset += length;
        }
        throw new InvalidDataException($"ISO root does not contain '{fileName}'.");
    }

    private static byte[] ReadRange(Stream source, uint sector, uint length, int maximumLength)
    {
        var offset = (long)sector * 2048;
        if (length > maximumLength || offset > source.Length || length > source.Length - offset)
            throw new InvalidDataException("ISO file range exceeds its size limit or the source image.");
        var bytes = new byte[(int)length];
        source.Position = offset;
        source.ReadExactly(bytes);
        return bytes;
    }

    private static uint UInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}

public sealed record Iso9660RootFile(long DirectoryRecordOffset, uint Sector, uint Length);
