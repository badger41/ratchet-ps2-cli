using System.Buffers.Binary;
using RatchetPs2.Core.LevelAssets;

namespace RatchetPs2.Games.UYA.Builders;

public static class UyaMobyClassLookupComposer
{
    private sealed record Section(int Offset, int Address, int Length, int Type, int Entry);

    public static byte[] Compose(ReadOnlySpan<byte> sourceOverlay, ReadOnlySpan<byte> assetHeader,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var header = LevelAssetReader.ReadHeader(assetHeader);
        var classes = LevelAssetReader.ReadModelDefinitions(assetHeader, header.MobyModelOffset, header.MobyModelCount);
        var maximumClass = classes.Select(value => value.ModelId).DefaultIfEmpty(0).Max();
        if (maximumClass < 0x2000) return sourceOverlay.ToArray();
        if (maximumClass >= 0x8000 || classes.Any(value => value.ModelId < 0))
            throw new InvalidDataException("UYA Moby classes must fit the signed native class list (0x0000–0x7FFF).");
        var capacity = maximumClass < 0x4000 ? 0x4000 : 0x8000;
        var reservedLength = capacity + 0x1000;
        var sections = ReadSections(sourceOverlay);
        var codeSection = sections.SingleOrDefault(value => value.Type == 1
            && value.Address <= value.Entry && value.Entry < (long)value.Address + value.Length)
            ?? throw Unsupported();
        var bytes = sourceOverlay.ToArray();
        var code = bytes.AsSpan(codeSection.Offset, codeSection.Length);
        var initializers = new List<int>();
        var heapInitializers = new List<(int High, int Low)>();
        for (var offset = 0; offset <= code.Length - 52; offset += 4)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((Read(code, offset) & 0xffff0000) == 0x3c040000
                && Read(code, offset + 4) == 0x2405ffff
                && (Read(code, offset + 8) & 0xffff0000) == 0x24840000
                && Read(code, offset + 12) >> 26 == 3
                && Read(code, offset + 16) == 0x24062000
                && (Read(code, offset + 20) & 0xffff0000) == 0x3c040000
                && Read(code, offset + 24) == 0x2405ffff
                && (Read(code, offset + 28) & 0xffff0000) == 0x24840000
                && Read(code, offset + 32) == Read(code, offset + 12)
                && Read(code, offset + 36) == 0x24060f00)
                initializers.Add(offset);
            if ((Read(code, offset) & 0xffff0000) == 0x3c040000
                && Read(code, offset + 4) == 0x3c02ffff
                && (Read(code, offset + 8) & 0xffff0000) == 0x3c060000
                && (Read(code, offset + 12) & 0xffff0000) == 0x8cc60000
                && Read(code, offset + 16) == 0x3442f000
                && (Read(code, offset + 20) & 0xffff0000) == 0x24840000
                && Read(code, offset + 28) == 0x00822024)
                heapInitializers.Add((offset, offset + 20));
            if ((Read(code, offset) & 0xffff0000) == 0x3c040000
                && Read(code, offset + 8) == 0x3c02ffff
                && Read(code, offset + 12) == 0x3c09fffc
                && Read(code, offset + 16) == 0x3c05fff9
                && Read(code, offset + 32) == 0x3442f000
                && (Read(code, offset + 36) & 0xffff0000) == 0x24840000
                && Read(code, offset + 48) == 0x00822024)
                heapInitializers.Add((offset, offset + 36));
        }
        if (initializers.Count != 1 || heapInitializers.Count != 1) throw Unsupported();
        var initializer = initializers[0];
        var heapInitializer = heapInitializers[0];
        var tableHigh = Read(code, initializer) & 0xffff;
        var tableLow = Read(code, initializer + 8) & 0xffff;
        var oldTable = checked((int)(tableHigh << 16) + unchecked((short)tableLow));
        var heapMarker = checked((int)((Read(code, heapInitializer.High) & 0xffff) << 16)
            + unchecked((short)Read(code, heapInitializer.Low)));
        var table = heapMarker & ~0xfff;
        if (table < 0x100000 || table > 0x1e00000 - reservedLength
            || sections.Any(value => (long)value.Address + value.Length > table)
            || !sections.Any(value => value.Type == 8 && oldTable >= value.Address
                && (long)oldTable + 0x2000 <= (long)value.Address + value.Length))
            throw Unsupported();

        var references = 0;
        var resets = new List<int>();
        var clearCall = Read(code, initializer + 12);
        var bootstrapAddress = checked(table + capacity);
        var bootstrapCall = 0x0c000000 | (uint)bootstrapAddress >> 2;
        for (var offset = 0; offset < code.Length; offset += 4)
        {
            var word = Read(code, offset);
            if (word >> 26 != 9 || (word & 0xffff) != tableLow) continue;
            var sourceRegister = (int)(word >> 21) & 31;
            if (!HasBaseLoad(code, offset, sourceRegister, tableHigh)) continue;
            if ((word >> 16 & 31) == 4 && offset <= code.Length - 12
                && Read(code, offset + 4) == clearCall
                && Read(code, offset + 8) == 0x24062000)
            {
                Write(code, offset + 4, bootstrapCall);
                resets.Add(offset);
            }
            else
            {
                // Shared LUI bases stay intact; the native reset initializes this pointer first.
                Write(code, offset, (word & 0x03ffffff) | 0x8c000000);
            }
            references++;
        }
        if (references < 8) throw Unsupported();
        if (!resets.Contains(initializer + 8)) throw Unsupported();
        var reservedHeapMarker = checked(heapMarker + reservedLength);
        Write(code, heapInitializer.High, 0x3c040000 | (uint)((reservedHeapMarker + 0x8000) >> 16));
        Write(code, heapInitializer.Low, 0x24840000 | ((uint)reservedHeapMarker & 0xffff));

        using var output = new MemoryStream(bytes.Length + capacity + 48);
        output.Write(bytes);
        // Keep the initializer outside BSS and reserve its page before the level arena.
        AppendSection(output, table, capacity + 32, codeSection.Entry);
        output.Write(Enumerable.Repeat((byte)0xff, capacity).ToArray());
        Span<byte> bootstrap = stackalloc byte[32];
        Write(bootstrap, 0, 0x3c040000 | tableHigh);                         // lui a0, old base high
        Write(bootstrap, 4, 0x3c060000 | (uint)((table + 0x8000) >> 16)); // lui a2, new base high
        Write(bootstrap, 8, 0x24c60000 | ((uint)table & 0xffff));          // addiu a2, a2, new base low
        Write(bootstrap, 12, 0xac860000 | tableLow);                     // sw a2, old base low(a0)
        Write(bootstrap, 16, 0x00c02025);                                // move a0, a2
        Write(bootstrap, 20, 0x34060000 | (uint)capacity);                 // ori a2, zero, capacity
        Write(bootstrap, 24, 0x08000000 | (clearCall & 0x03ffffff));       // tail-call native fill
        Write(bootstrap, 28, 0);                                        // delay slot
        output.Write(bootstrap);
        return output.ToArray();
    }

    private static bool HasBaseLoad(ReadOnlySpan<byte> code, int offset, int register, uint high)
    {
        for (var previous = offset - 4; previous >= Math.Max(0, offset - 128); previous -= 4)
        {
            var word = Read(code, previous);
            var opcode = word >> 26;
            var destination = opcode == 0 ? (int)(word >> 11) & 31
                : opcode is >= 8 and <= 15 or >= 24 and <= 27 or 30 or >= 32 and <= 39 or 55
                    ? (int)(word >> 16) & 31 : 0;
            if (destination == register)
                return word == (0x3c000000 | (uint)register << 16 | high);
            if (opcode == 3) return false;
        }
        return false;
    }

    private static List<Section> ReadSections(ReadOnlySpan<byte> bytes)
    {
        var result = new List<Section>();
        for (var offset = 0; offset < bytes.Length;)
        {
            if (offset > bytes.Length - 16) throw Unsupported();
            var address = checked((int)Read(bytes, offset));
            var length = checked((int)Read(bytes, offset + 4));
            var type = checked((int)Read(bytes, offset + 8));
            var entry = checked((int)Read(bytes, offset + 12));
            offset += 16;
            if (address <= 0 || (address & 3) != 0 || length <= 0 || (length & 3) != 0
                || length > bytes.Length - offset || result.Any(value => value.Entry != entry)) throw Unsupported();
            result.Add(new(offset, address, length, type, entry));
            offset += length;
        }
        return result;
    }

    private static void AppendSection(Stream output, int address, int length, int entry)
    {
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, address);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], length);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], entry);
        output.Write(header);
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void Write(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
    private static InvalidDataException Unsupported() => new(
        "This UYA overlay does not have a verified Moby lookup and level-arena layout. Classes above 0x1FFF cannot be packed safely for this source level.");
}
