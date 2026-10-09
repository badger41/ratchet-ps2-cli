using RatchetPs2.Core.Executables;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.DL.Executables;

/// <summary>Reconstructs analysis ELFs from Deadlocked's executable copy records.</summary>
public static class DlElfBuilder
{
    public static byte[] BuildBoot(ReadOnlySpan<byte> packedBoot)
    {
        if (packedBoot.Length < 52 || packedBoot[0] != 0x7f || !packedBoot.Slice(1, 3).SequenceEqual("ELF"u8))
            throw new InvalidDataException("The boot executable is not an ELF file.");
        var wadOffset = packedBoot.IndexOf("WAD"u8);
        if (wadOffset < 0)
            throw new InvalidDataException("The boot ELF has no packed executable WAD.");
        return BuildRecords(WadCompression.Decompress(packedBoot[wadOffset..]), boot: true);
    }

    public static byte[] BuildOverlay(ReadOnlySpan<byte> records) => BuildRecords(records, boot: false);

    private static byte[] BuildRecords(ReadOnlySpan<byte> bytes, bool boot)
    {
        var code = DlCodeSegmentReader.Read(bytes);
        var names = boot
            ? new[] { ".reginfo", ".vutext", "core.text", "core.data", "core.rdata", "core.bss", "core.lit",
                ".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text", "patch.data", "net.text", "net.nostomp" }
            : new[] { ".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text", "net.text" };
        if (code.Records.Count != names.Length || code.UnparsedTail.Length != 0)
            throw new InvalidDataException($"Unsupported DL executable record layout ({code.Records.Count} records, {code.UnparsedTail.Length} trailing bytes).");
        var entry = code.Records[0].EntrypointAddress;
        var sections = new List<Elf32Section>();
        for (var i = 0; i < names.Length; i++)
        {
            var record = code.Records[i];
            var name = names[i];
            if (record.EntrypointAddress != entry)
                throw new InvalidDataException("Executable records disagree about the entry point.");
            var expectedType = name switch
            {
                ".reginfo" or "patch.data" => 0x70000006,
                "core.bss" => 8,
                ".bss" => record.Type is 1 or 8 ? record.Type : 8,
                _ => 1
            };
            if (record.Type != expectedType)
                throw new InvalidDataException($"Unexpected section type for {name}: 0x{record.Type:X}.");
            var flags = name switch
            {
                ".reginfo" => 0u,
                "net.text" => 7u,
                ".vutext" or "core.text" or ".text" or "patch.data" => 6u,
                "core.rdata" or "lvl.vtbl" or "lvl.camvtbl" or "lvl.sndvtbl" => 2u,
                "core.bss" or "core.lit" or ".lit" or ".bss" => 0xf0000003u,
                _ => 3u
            };
            var alignment = name switch
            {
                ".reginfo" or "patch.data" => 4u,
                ".vutext" or "core.rdata" or "net.text" => 16u,
                "core.data" => 128u,
                "core.lit" or "net.nostomp" => 8u,
                "lvl.vtbl" or "lvl.camvtbl" or "lvl.sndvtbl" => 1u,
                _ => 64u
            };
            // DL stores bytes even for NOBITS copy records. Keep them in the load
            // image; section type alone does not describe its runtime copy semantics.
            // patch.data is exposed as PROGBITS so GNU MIPS tools accept the ELF.
            sections.Add(new(name, name == "patch.data" ? 1u : (uint)record.Type, flags,
                record.InjectAddress, alignment, name is ".reginfo" or "patch.data" ? 1u : 0u,
                name == ".reginfo" ? -1 : name.StartsWith("net.", StringComparison.Ordinal) ? 1 : 0,
                record.PayloadBytes));
        }
        return Elf32Writer.Write(new(entry, 0x20924001, sections));
    }
}
