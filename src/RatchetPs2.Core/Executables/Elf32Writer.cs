using System.Text;

namespace RatchetPs2.Core.Executables;

/// <summary>Writes little-endian MIPS ELF images with explicit loadable section payloads.</summary>
public static class Elf32Writer
{
    public static byte[] Write(Elf32Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sections = image.Sections;
        if (sections.Count == 0 || sections.Count > ushort.MaxValue - 2)
            throw new ArgumentException("An ELF requires a representable section table.", nameof(image));
        var segmentCount = sections.Max(section => section.Segment) + 1;
        if (segmentCount is < 0 or > 32 || sections.Any(section => section.Segment < -1))
            throw new ArgumentException("Invalid ELF segment assignment.", nameof(image));

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        var offsets = new uint[sections.Count];
        var bases = new uint[segmentCount];
        var starts = new uint[segmentCount];
        var sizes = new uint[segmentCount];
        var lastSegment = -1;
        output.SetLength(4096);
        output.Position = 4096;

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Name.Contains('\0') || section.Name.Any(character => character > 127))
                throw new InvalidDataException("ELF section names must be ASCII without embedded nulls.");
            if (section.Bytes.Length == 0) continue;
            if (section.Segment < lastSegment)
                throw new InvalidDataException("ELF sections must be grouped in segment order.");
            lastSegment = section.Segment;
            if (section.Segment >= 0)
            {
                var segment = section.Segment;
                if (sizes[segment] == 0)
                {
                    Align(output, 4096);
                    bases[segment] = section.Address;
                    starts[segment] = checked((uint)output.Position);
                }
                var relative = (long)section.Address - bases[segment];
                if (relative < sizes[segment])
                    throw new InvalidDataException("ELF load sections overlap or are out of order.");
                var position = checked(starts[segment] + relative);
                if (position + section.Bytes.Length > 256 * 1024 * 1024 ||
                    (ulong)section.Address + (uint)section.Bytes.Length > uint.MaxValue)
                    throw new InvalidDataException("ELF load layout exceeds the supported image size.");
                output.SetLength(position);
                output.Position = position;
                sizes[segment] = checked((uint)(relative + section.Bytes.Length));
            }
            offsets[i] = checked((uint)output.Position);
            writer.Write(section.Bytes);
        }

        var namesStart = checked((uint)output.Position);
        writer.Write((byte)0);
        var names = new uint[sections.Count];
        for (var i = 0; i < sections.Count; i++)
        {
            names[i] = checked((uint)output.Position - namesStart);
            writer.Write(Encoding.ASCII.GetBytes(sections[i].Name));
            writer.Write((byte)0);
        }
        var stringTableName = checked((uint)output.Position - namesStart);
        writer.Write(".shstrtab\0"u8);
        var namesSize = checked((uint)output.Position - namesStart);
        Align(output, 4);
        var sectionTable = checked((uint)output.Position);
        writer.Write(new byte[40]);
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            WriteSection(writer, names[i], section.Type, section.Flags, section.Address,
                offsets[i], checked((uint)section.Bytes.Length), section.Alignment, section.EntrySize);
        }
        WriteSection(writer, stringTableName, 3, 0, 0, namesStart, namesSize, 1, 0);

        output.Position = 0;
        writer.Write(new byte[] { 0x7f, 0x45, 0x4c, 0x46, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        writer.Write((ushort)2);
        writer.Write((ushort)8);
        writer.Write(1u);
        writer.Write(image.EntryPoint);
        writer.Write(segmentCount > 0 ? 52u : 0u);
        writer.Write(sectionTable);
        writer.Write(image.Flags);
        writer.Write((ushort)52);
        writer.Write((ushort)32);
        writer.Write((ushort)segmentCount);
        writer.Write((ushort)40);
        writer.Write((ushort)(sections.Count + 2));
        writer.Write((ushort)(sections.Count + 1));
        for (var i = 0; i < segmentCount; i++)
        {
            writer.Write(1u);
            writer.Write(starts[i]);
            writer.Write(bases[i]);
            writer.Write(bases[i]);
            writer.Write(sizes[i]);
            writer.Write(sizes[i]);
            writer.Write(7u);
            writer.Write(4096u);
        }
        return output.ToArray();
    }

    private static void Align(MemoryStream output, int alignment)
    {
        var position = (output.Position + alignment - 1) / alignment * alignment;
        output.SetLength(position);
        output.Position = position;
    }

    private static void WriteSection(BinaryWriter writer, uint name, uint type, uint flags,
        uint address, uint offset, uint size, uint alignment, uint entrySize)
    {
        writer.Write(name);
        writer.Write(type);
        writer.Write(flags);
        writer.Write(address);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(alignment);
        writer.Write(entrySize);
    }
}
