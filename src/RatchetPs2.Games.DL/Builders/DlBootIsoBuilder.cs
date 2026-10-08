using System.Buffers.Binary;
using RatchetPs2.Core.Disc;
using RatchetPs2.Games.DL.Executables;

namespace RatchetPs2.Games.DL.Builders;

/// <summary>Builds a retail DL disc copy with an appended, directly loadable boot ELF.</summary>
public static class DlBootIsoBuilder
{
    private const int SectorSize = 2048;

    public static void Build(Stream source, Stream destination, ReadOnlySpan<byte> bootElf)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination) || !source.CanRead || !source.CanSeek ||
            !destination.CanRead || !destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("Distinct readable/seekable input and readable/writable/seekable output streams are required.");
        ValidateElf(bootElf);
        var metadata = PlayStation2DiscReader.Read(source);
        if (metadata.Serial != "SCUS-97465" || metadata.Revision != "1.00")
            throw new NotSupportedException("Boot ISO builds currently support Deadlocked SCUS-97465 revision 1.00.");
        if (source.Length % SectorSize != 0)
            throw new InvalidDataException("Source ISO must contain complete sectors.");
        var entry = Iso9660RootReader.FindFile(source, DlExecutableReader.BootFileName);
        if ((long)entry.Sector * SectorSize + entry.Length > source.Length)
            throw new InvalidDataException("Source boot extent is outside the ISO.");
        var descriptorOffsets = FindVolumeDescriptors(source);
        var bootSector = checked((uint)(source.Length / SectorSize));
        var outputSize = checked(source.Length + ((bootElf.Length + (long)SectorSize - 1) / SectorSize) * SectorSize);
        var outputSectors = checked((uint)(outputSize / SectorSize));

        source.Position = 0;
        destination.Position = 0;
        destination.SetLength(0);
        source.CopyTo(destination, 1024 * 1024);
        destination.Write(bootElf);
        destination.SetLength(outputSize);
        WriteBothEndian(destination, entry.DirectoryRecordOffset + 2, bootSector);
        WriteBothEndian(destination, entry.DirectoryRecordOffset + 10, (uint)bootElf.Length);
        foreach (var offset in descriptorOffsets) WriteBothEndian(destination, offset + 80, outputSectors);
        destination.Flush();

        var installed = Iso9660RootReader.ReadFile(destination, DlExecutableReader.BootFileName);
        if (!bootElf.SequenceEqual(installed))
            throw new IOException("Installed boot ELF failed byte-for-byte verification.");
    }

    public static void ValidateElf(ReadOnlySpan<byte> elf)
    {
        if (elf.Length < 52 || elf.Length > 32 * 1024 * 1024 ||
            elf[0] != 127 || !elf.Slice(1, 3).SequenceEqual("ELF"u8) || elf[4] != 1 || elf[5] != 1 ||
            U16(elf, 16) != 2 || U16(elf, 18) != 8 || U32(elf, 20) != 1 ||
            U16(elf, 40) != 52 || U16(elf, 42) != 32)
            throw new InvalidDataException("Boot input must be a 32-bit little-endian MIPS executable ELF.");
        var table = U32(elf, 28);
        var count = U16(elf, 44);
        if (count == 0 || (long)table + count * 32 > elf.Length)
            throw new InvalidDataException("Boot ELF program table is invalid.");
        var entry = U32(elf, 24);
        var executableEntry = false;
        for (var i = 0; i < count; i++)
        {
            var header = elf.Slice(checked((int)table + i * 32), 32);
            if (U32(header, 0) != 1) continue;
            var offset = U32(header, 4);
            var address = U32(header, 8);
            var fileSize = U32(header, 16);
            var memorySize = U32(header, 20);
            if (fileSize > memorySize || (long)offset + fileSize > elf.Length ||
                (ulong)address + memorySize > 32 * 1024 * 1024)
                throw new InvalidDataException("Boot ELF load segment is outside the file or PS2 EE memory.");
            if (entry >= address && (ulong)entry < (ulong)address + fileSize && (U32(header, 24) & 1) != 0)
                executableEntry = true;
        }
        if (!executableEntry) throw new InvalidDataException("Boot ELF entry point is not in an executable load segment.");
    }

    private static List<long> FindVolumeDescriptors(Stream source)
    {
        var offsets = new List<long>();
        var descriptor = new byte[SectorSize];
        for (var sector = 16; sector < 32; sector++)
        {
            source.Position = sector * (long)SectorSize;
            source.ReadExactly(descriptor);
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8))
                throw new InvalidDataException("Invalid ISO volume descriptor sequence.");
            if (descriptor[0] == 2)
                throw new NotSupportedException("Supplementary ISO directory trees are not supported by the DL boot builder.");
            if (descriptor[0] == 1) offsets.Add(sector * (long)SectorSize);
            if (descriptor[0] == 255 && offsets.Count == 1) return offsets;
        }
        throw new InvalidDataException("ISO must contain one primary volume and a descriptor terminator.");
    }

    private static void WriteBothEndian(Stream output, long offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[4..], value);
        output.Position = offset;
        output.Write(bytes);
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
