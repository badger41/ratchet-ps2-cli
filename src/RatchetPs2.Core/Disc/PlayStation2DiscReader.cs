using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace RatchetPs2.Core.Disc;

public static partial class PlayStation2DiscReader
{
    private const int SectorSize = 2048;

    public static PlayStation2DiscMetadata Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The disc image stream must be readable and seekable.", nameof(source));

        var configuration = ReadSystemConfiguration(source);
        var serial = ParseValue(configuration, BootPattern(), "BOOT2 serial").Replace('_', '-').Replace(".", "");
        var revision = ParseValue(configuration, VersionPattern(), "VER");
        var videoMode = ParseValue(configuration, VideoModePattern(), "VMODE");
        var region = videoMode.Equals("NTSC", StringComparison.OrdinalIgnoreCase)
            ? "NTSC-U"
            : videoMode.ToUpperInvariant();
        return new(region, revision, serial);
    }

    private static string ReadSystemConfiguration(Stream source)
    {
        Span<byte> descriptor = stackalloc byte[SectorSize];
        source.Position = 16L * SectorSize;
        source.ReadExactly(descriptor);
        if (descriptor[0] != 1 || !descriptor[1..6].SequenceEqual("CD001"u8))
            throw new InvalidDataException("The selected file is not an ISO 9660 disc image.");

        var root = descriptor[156..];
        var rootSector = BinaryPrimitives.ReadUInt32LittleEndian(root[2..]);
        var rootLength = BinaryPrimitives.ReadUInt32LittleEndian(root[10..]);
        if (rootLength is 0 or > 16 * 1024 * 1024) throw new InvalidDataException("ISO root directory is invalid.");
        var directory = GC.AllocateUninitializedArray<byte>((int)rootLength);
        source.Position = (long)rootSector * SectorSize;
        source.ReadExactly(directory);

        for (var offset = 0; offset < directory.Length;)
        {
            var recordLength = directory[offset];
            if (recordLength == 0)
            {
                offset = ((offset / SectorSize) + 1) * SectorSize;
                continue;
            }
            if (offset + recordLength > directory.Length || recordLength < 34) break;
            var record = directory.AsSpan(offset, recordLength);
            var nameLength = record[32];
            if (33 + nameLength <= record.Length)
            {
                var name = Encoding.ASCII.GetString(record.Slice(33, nameLength));
                if (name.Equals("SYSTEM.CNF;1", StringComparison.OrdinalIgnoreCase))
                {
                    var sector = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
                    if (length > 64 * 1024) throw new InvalidDataException("SYSTEM.CNF is unexpectedly large.");
                    var bytes = GC.AllocateUninitializedArray<byte>((int)length);
                    source.Position = (long)sector * SectorSize;
                    source.ReadExactly(bytes);
                    return Encoding.ASCII.GetString(bytes);
                }
            }
            offset += recordLength;
        }
        throw new InvalidDataException("ISO does not contain SYSTEM.CNF.");
    }

    private static string ParseValue(string configuration, Regex pattern, string name)
    {
        var match = pattern.Match(configuration);
        return match.Success ? match.Groups[1].Value : throw new InvalidDataException($"SYSTEM.CNF does not contain {name}.");
    }

    [GeneratedRegex(@"BOOT2\s*=\s*cdrom0:\\([A-Z]{4}_[0-9]{3}\.[0-9]{2});1", RegexOptions.IgnoreCase)]
    private static partial Regex BootPattern();

    [GeneratedRegex(@"^\s*VER\s*=\s*([^\s\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^\s*VMODE\s*=\s*([^\s\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex VideoModePattern();
}
