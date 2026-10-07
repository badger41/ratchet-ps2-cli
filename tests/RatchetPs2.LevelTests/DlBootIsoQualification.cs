using System.Buffers.Binary;
using System.Text;
using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Executables;
using RatchetPs2.Games.DL.Builders;

internal static class DlBootIsoQualification
{
    public static void RunUnitTests()
    {
        using var source = MakeDisc();
        using var output = new MemoryStream();
        var elf = Elf32Writer.Write(new(0x100000, 0x20924001,
            [new(".text", 1, 6, 0x100000, 4, 0, 0, [1, 2, 3, 4])]));
        var original = source.ToArray();
        DlBootIsoBuilder.Build(source, output, elf);
        Require(source.ToArray().AsSpan().SequenceEqual(original), "source mutation");
        Compare(source, output, elf);
        elf[4096] = 5;
        DlBootIsoBuilder.Build(source, output, elf);
        Compare(source, output, elf);
        try
        {
            DlBootIsoBuilder.Build(source, source, elf);
            throw new InvalidOperationException("Identical input and output accepted.");
        }
        catch (ArgumentException) { }
        var invalid = (byte[])elf.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(24), 0x2000000);
        try
        {
            DlBootIsoBuilder.ValidateElf(invalid);
            throw new InvalidOperationException("Invalid entry point accepted.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("DL boot ISO synthetic checks passed.");
    }

    public static void VerifyFiles(string sourcePath, string outputPath, string elfPath)
    {
        using var source = File.OpenRead(sourcePath);
        using var output = File.OpenRead(outputPath);
        Compare(source, output, File.ReadAllBytes(elfPath));
        Console.WriteLine("Installed boot ELF matches; original ISO bytes are preserved except the boot directory extent/size and volume size.");
    }

    private static void Compare(Stream source, Stream output, byte[] elf)
    {
        var entry = Iso9660RootReader.FindFile(source, "SCUS_974.65");
        var installed = Iso9660RootReader.FindFile(output, "SCUS_974.65");
        Require(installed.Sector * 2048L == source.Length, "appended boot location");
        Require(installed.Length == elf.Length, "installed ELF size");
        Require(Iso9660RootReader.ReadFile(output, "SCUS_974.65").AsSpan().SequenceEqual(elf), "installed ELF payload");
        Require(output.Length == source.Length + ((elf.Length + 2047L) / 2048) * 2048, "output length");
        var volume = new byte[8];
        output.Position = 16 * 2048 + 80;
        output.ReadExactly(volume);
        Require(BinaryPrimitives.ReadUInt32LittleEndian(volume) == output.Length / 2048 &&
            BinaryPrimitives.ReadUInt32BigEndian(volume.AsSpan(4)) == output.Length / 2048, "both-endian volume size");
        var original = new byte[1024 * 1024];
        var rebuilt = new byte[original.Length];
        source.Position = output.Position = 0;
        long offset = 0;
        while (offset < source.Length)
        {
            var count = (int)Math.Min(original.Length, source.Length - offset);
            source.ReadExactly(original.AsSpan(0, count));
            output.ReadExactly(rebuilt.AsSpan(0, count));
            // Mask only the exact fields which the builder is permitted to update.
            foreach (var (start, length) in new[] { (16L * 2048 + 80, 8), (entry.DirectoryRecordOffset + 2, 16) })
            {
                var begin = Math.Max(offset, start);
                var end = Math.Min(offset + count, start + length);
                if (begin < end)
                    original.AsSpan((int)(begin - offset), (int)(end - begin)).CopyTo(rebuilt.AsSpan((int)(begin - offset)));
            }
            Require(original.AsSpan(0, count).SequenceEqual(rebuilt.AsSpan(0, count)), $"original disc bytes at 0x{offset:X}");
            offset += count;
        }
    }

    private static MemoryStream MakeDisc()
    {
        var data = new byte[40 * 2048];
        var pvd = data.AsSpan(16 * 2048, 2048);
        pvd[0] = 1;
        "CD001"u8.CopyTo(pvd[1..]);
        pvd[6] = 1;
        WriteRecord(pvd[156..], "\0", 20, 2048);
        pvd[181] = 2;
        data[17 * 2048] = 255;
        "CD001"u8.CopyTo(data.AsSpan(17 * 2048 + 1));
        var config = Encoding.ASCII.GetBytes("BOOT2 = cdrom0:\\SCUS_974.65;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n");
        var root = data.AsSpan(20 * 2048, 2048);
        var count = WriteRecord(root, "SYSTEM.CNF;1", 21, (uint)config.Length);
        WriteRecord(root[count..], "SCUS_974.65;1", 22, 52);
        config.CopyTo(data.AsSpan(21 * 2048));
        return new MemoryStream(data, writable: false);
    }

    private static int WriteRecord(Span<byte> target, string name, uint sector, uint length)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var size = (33 + nameBytes.Length + 1) & ~1;
        target[0] = (byte)size;
        BinaryPrimitives.WriteUInt32LittleEndian(target[2..], sector);
        BinaryPrimitives.WriteUInt32BigEndian(target[6..], sector);
        BinaryPrimitives.WriteUInt32LittleEndian(target[10..], length);
        BinaryPrimitives.WriteUInt32BigEndian(target[14..], length);
        target[32] = (byte)nameBytes.Length;
        nameBytes.CopyTo(target[33..]);
        return size;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"DL boot ISO check failed: {message}.");
    }
}
