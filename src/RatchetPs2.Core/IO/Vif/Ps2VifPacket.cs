namespace RatchetPs2.Core.IO.Vif;

public readonly record struct Ps2VifCycle(byte CycleLength, byte WriteLength)
{
    public static Ps2VifCycle OneToOne { get; } = new(1, 1);
    public int EffectiveWriteLength => WriteLength == 0 ? 256 : WriteLength;
    public ushort Immediate => checked((ushort)(CycleLength | (WriteLength << 8)));

    public static Ps2VifCycle FromImmediate(int immediate) =>
        new((byte)immediate, (byte)(immediate >> 8));
}

public sealed record Ps2VifPacketSpan(
    int Offset,
    ushort Immediate,
    byte Num,
    byte CommandByte,
    int Command,
    bool IsUnpack,
    int PayloadLength,
    int TotalLength)
{
    public int RawPayloadLength { get; init; } = PayloadLength;
    public Ps2VifCycle Cycle { get; init; } = Ps2VifCycle.OneToOne;
    public int EffectiveNum => Num == 0 ? 256 : Num;
    public int Irq => CommandByte >> 7;
    public int VnVl => IsUnpack ? Command & 0x0F : 0;
    public int DestinationAddress => Immediate & 0x03FF;
    public int AlignedPayloadLength => PayloadLength;
    public Ps2VifUnpackFormat? UnpackFormat =>
        Ps2VifPacket.TryGetUnpackFormat(Command, out var format) ? format : null;
    public string Kind => IsUnpack && UnpackFormat is { } format
        ? $"UNPACK_{format.Name}"
        : Ps2VifPacket.GetCommandName(Command);
}

public readonly record struct Ps2VifUnpackFormat(
    string Name,
    int ComponentCount,
    int ComponentByteSize,
    int BytesPerElement);

public static class Ps2VifPacket
{
    public static void WriteHeader(Stream stream, ushort immediate, byte num, byte commandByte)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.WriteByte((byte)(immediate & 0xFF));
        stream.WriteByte((byte)(immediate >> 8));
        stream.WriteByte(num);
        stream.WriteByte(commandByte);
    }

    public static List<Ps2VifPacketSpan> ReadSpans(
        ReadOnlySpan<byte> vifData,
        Ps2VifCycle? initialCycle = null)
    {
        var packets = new List<Ps2VifPacketSpan>();
        var cycle = initialCycle ?? Ps2VifCycle.OneToOne;
        var offset = 0;
        while (offset + 4 <= vifData.Length)
        {
            var immediate = vifData[offset] | (vifData[offset + 1] << 8);
            var num = vifData[offset + 2];
            var commandByte = vifData[offset + 3];
            var command = commandByte & 0x7F;
            var rawPayloadLength = GetPayloadLength(command, immediate, num, cycle);
            var alignedPayloadLength = Align(rawPayloadLength, 4);
            var availablePayloadLength = vifData.Length - offset - 4;
            if (alignedPayloadLength > availablePayloadLength)
            {
                if (command < 0x60)
                {
                    break;
                }

                alignedPayloadLength = Math.Max(availablePayloadLength, 0);
            }

            var totalLength = 4 + alignedPayloadLength;

            packets.Add(new Ps2VifPacketSpan(
                offset,
                checked((ushort)immediate),
                num,
                commandByte,
                command,
                IsUnpackCommand(command),
                alignedPayloadLength,
                totalLength)
            {
                RawPayloadLength = rawPayloadLength,
                Cycle = cycle,
            });

            offset += totalLength;
            if (command == 0x01)
            {
                cycle = Ps2VifCycle.FromImmediate(immediate);
            }
        }

        return packets;
    }

    public static bool IsUnpackCommand(int command) => command is >= 0x60 and <= 0x7F;

    public static bool TryGetUnpackPayloadLength(int command, int num, out int payloadLength)
    {
        return TryGetUnpackPayloadLength(command, num, Ps2VifCycle.OneToOne, out payloadLength);
    }

    public static bool TryGetUnpackPayloadLength(
        int command,
        int num,
        Ps2VifCycle cycle,
        out int payloadLength)
    {
        if (!TryGetUnpackFormat(command, out var format))
        {
            payloadLength = 0;
            return false;
        }

        payloadLength = checked(GetUnpackSourceElementCount(num, cycle) * format.BytesPerElement);
        return true;
    }

    public static int GetUnpackSourceElementCount(int num, Ps2VifCycle cycle)
    {
        if (num is < 0 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(num), num, "VIF NUM must fit in one byte.");
        }

        var outputElementCount = num == 0 ? 256 : num;
        var writeLength = cycle.EffectiveWriteLength;
        if (writeLength <= cycle.CycleLength)
        {
            return outputElementCount;
        }

        return checked(
            cycle.CycleLength * (outputElementCount / writeLength)
            + Math.Min(outputElementCount % writeLength, cycle.CycleLength));
    }

    public static bool TryGetUnpackFormat(int command, out Ps2VifUnpackFormat format)
    {
        format = default;
        if (!IsUnpackCommand(command))
        {
            return false;
        }

        var unpackFormat = command & 0x0F;
        var componentCount = ((unpackFormat >> 2) & 0x03) + 1;
        var valueSizeBytes = (unpackFormat & 0x03) switch
        {
            0 => 4,
            1 => 2,
            2 => 1,
            3 when componentCount == 4 => 2,
            _ => 0,
        };
        if (valueSizeBytes == 0)
        {
            return false;
        }

        var componentName = componentCount == 1 ? "S" : $"V{componentCount}";
        var valueName = (unpackFormat & 0x03) == 3 ? "5_PACKED" : (valueSizeBytes * 8).ToString();
        format = new(
            $"{componentName}_{valueName}",
            componentCount,
            valueSizeBytes,
            (unpackFormat & 0x03) == 3 ? 2 : componentCount * valueSizeBytes);
        return true;
    }

    public static int GetPayloadLength(int command, int num) =>
        GetPayloadLength(command, immediate: 0, num, Ps2VifCycle.OneToOne);

    public static int GetPayloadLength(int command, int immediate, int num)
    {
        return GetPayloadLength(command, immediate, num, Ps2VifCycle.OneToOne);
    }

    public static int GetPayloadLength(
        int command,
        int immediate,
        int num,
        Ps2VifCycle cycle)
    {
        if (TryGetUnpackPayloadLength(command, num, cycle, out var unpackPayloadLength))
        {
            return unpackPayloadLength;
        }

        var effectiveNum = num == 0 ? 256 : num;
        return command switch
        {
            0x20 => 4,
            0x30 or 0x31 => 16,
            0x4A => checked(effectiveNum * 8),
            0x50 or 0x51 => checked(immediate * 16),
            _ => 0,
        };
    }

    public static byte[] BuildBytes(
        ushort immediate,
        byte num,
        byte commandByte,
        ReadOnlySpan<byte> payload,
        Ps2VifCycle? cycle = null)
    {
        var command = commandByte & 0x7F;
        var isUnpack = IsUnpackCommand(command);
        var hasUnpackFormat = TryGetUnpackFormat(command, out var unpackFormat);
        if ((isUnpack && !hasUnpackFormat)
            || (!isUnpack && GetCommandName(command) == "UNKNOWN"))
        {
            throw new InvalidDataException($"Cannot build unsupported VIF command 0x{command:X2}.");
        }
        var commandName = hasUnpackFormat ? $"UNPACK_{unpackFormat.Name}" : GetCommandName(command);

        var expectedPayloadLength = GetPayloadLength(
            command,
            immediate,
            num,
            cycle ?? Ps2VifCycle.OneToOne);
        if (payload.Length != expectedPayloadLength)
        {
            throw new InvalidDataException(
                $"VIF {commandName} expects {expectedPayloadLength} payload bytes, but received {payload.Length}.");
        }

        var bytes = new byte[4 + Align(payload.Length, 4)];
        bytes[0] = (byte)immediate;
        bytes[1] = (byte)(immediate >> 8);
        bytes[2] = num;
        bytes[3] = commandByte;
        payload.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    public static byte[] BuildFromTemplate(
        Ps2VifPacketSpan template,
        byte num,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(template);
        return BuildBytes(template.Immediate, num, template.CommandByte, payload, template.Cycle);
    }

    public static byte[] ReplacePacket(
        ReadOnlySpan<byte> vifData,
        Ps2VifPacketSpan template,
        byte num,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Offset < 0
            || template.TotalLength < 4
            || template.TotalLength != 4 + Align(template.RawPayloadLength, 4)
            || template.Offset > vifData.Length - template.TotalLength)
        {
            throw new ArgumentException("VIF packet template is outside the source stream.", nameof(template));
        }
        if (vifData[template.Offset] != (byte)template.Immediate
            || vifData[template.Offset + 1] != (byte)(template.Immediate >> 8)
            || vifData[template.Offset + 2] != template.Num
            || vifData[template.Offset + 3] != template.CommandByte)
        {
            throw new ArgumentException("VIF packet template does not match the source stream.", nameof(template));
        }

        var replacement = BuildFromTemplate(template, num, payload);
        var result = new byte[checked(vifData.Length - template.TotalLength + replacement.Length)];
        vifData[..template.Offset].CopyTo(result);
        replacement.CopyTo(result.AsSpan(template.Offset));
        vifData[(template.Offset + template.TotalLength)..]
            .CopyTo(result.AsSpan(template.Offset + replacement.Length));
        return result;
    }

    public static string GetCommandName(int command)
    {
        return command switch
        {
            0x00 => "NOP",
            0x01 => "STCYCL",
            0x02 => "OFFSET",
            0x03 => "BASE",
            0x04 => "ITOP",
            0x05 => "STMOD",
            0x06 => "MSKPATH3",
            0x07 => "MARK",
            0x10 => "FLUSHE",
            0x11 => "FLUSH",
            0x13 => "FLUSHA",
            0x14 => "MSCAL",
            0x15 => "MSCALF",
            0x17 => "MSCNT",
            0x20 => "STMASK",
            0x30 => "STROW",
            0x31 => "STCOL",
            0x4A => "MPG",
            0x50 => "DIRECT",
            0x51 => "DIRECTHL",
            _ => "UNKNOWN",
        };
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }
}
