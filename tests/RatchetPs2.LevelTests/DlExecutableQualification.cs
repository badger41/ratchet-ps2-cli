using System.Buffers.Binary;
using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Executables;
using RatchetPs2.Games.DL.Executables;

internal static class DlExecutableQualification
{
    public static void RunUnitTests()
    {
        var records = MakeOverlayRecords();
        var elf = DlElfBuilder.BuildOverlay(records);
        Require(elf.AsSpan(0, 4).SequenceEqual(new byte[] { 127, 69, 76, 70 }), "ELF magic");
        Require(Read(elf, 24) == 0x100000, "entry point");
        Require(BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(44)) == 2, "load segment count");
        var programs = (int)Read(elf, 28);
        Require(Read(elf, programs + 8) == 0x100000, "first load address");
        Require(Read(elf, programs + 16) == 28, "first load size");
        Require(Read(elf, programs + 32 + 8) == 0x200000, "network load address");
        var payload = (int)Read(elf, programs + 4);
        for (var i = 0; i < 7; i++) Require(Read(elf, payload + i * 4) == (uint)(i + 1), "record payload");
        var table = (int)Read(elf, 32);
        Require(Read(elf, table + 2 * 40 + 4) == 8, "NOBITS record type");
        Require(Read(elf, table + 2 * 40 + 20) == 4, "NOBITS payload size");

        Reject(() => DlElfBuilder.BuildOverlay(records[..^1]));
        Reject(() => DlElfBuilder.BuildOverlay([.. records, 1]));
        Reject(() => DlElfBuilder.BuildBoot(new byte[52]));
        var disagreement = (byte[])records.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(disagreement.AsSpan(32), 0x1234);
        Reject(() => DlElfBuilder.BuildOverlay(disagreement));
        var wrongType = (byte[])records.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongType.AsSpan(8), 7);
        Reject(() => DlElfBuilder.BuildOverlay(wrongType));
        var overlapping = new Elf32Section[]
        {
            new(".text", 1, 6, 0x100000, 4, 0, 0, [1, 2, 3, 4]),
            new(".data", 1, 3, 0x100002, 4, 0, 0, [5, 6, 7, 8])
        };
        Reject(() => Elf32Writer.Write(new(0x100000, 0, overlapping)));
        using var truncatedDisc = new MemoryStream(new byte[16 * 2048 + 20]);
        Reject(() => Iso9660RootReader.ReadFile(truncatedDisc, DlExecutableReader.BootFileName));
        Console.WriteLine("DL executable reconstruction unit checks passed.");
    }

    public static void CompareReference(string isoPath, string referenceDirectory)
    {
        using var iso = File.OpenRead(isoPath);
        var boot = DlExecutableReader.ReadBootElf(iso);
        var legacyReference = File.Exists(Path.Combine(referenceDirectory, "boot_elf.elf"));
        Require(boot.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(referenceDirectory,
            legacyReference ? "boot_elf.elf" : "boot.elf"))), "boot reference bytes");
        var count = 1;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(referenceDirectory, "levels"), "overlay.elf", SearchOption.AllDirectories))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(path))!;
            if ((folder == "code") == legacyReference) continue;
            var level = legacyReference
                ? int.Parse(folder.AsSpan(0, folder.IndexOf('_')))
                : int.Parse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!);
            var overlay = DlExecutableReader.ReadOverlayElf(iso, level);
            Require(overlay.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"level {level} reference bytes");
            count++;
        }
        Require(count == 48, $"expected 48 retail reference ELFs, found {count}");
        Console.WriteLine($"All {count} DL boot/overlay ELFs exactly match the supplied reference.");
    }

    private static byte[] MakeOverlayRecords()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        for (var i = 0; i < 8; i++)
        {
            writer.Write(i == 7 ? 0x200000u : 0x100000u + (uint)i * 4);
            writer.Write(4);
            writer.Write(i == 1 ? 8 : 1);
            writer.Write(0x100000u);
            writer.Write((uint)i + 1);
        }
        return stream.ToArray();
    }

    private static uint Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"DL executable check failed: {message}.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid executable input was accepted.");
    }
}
