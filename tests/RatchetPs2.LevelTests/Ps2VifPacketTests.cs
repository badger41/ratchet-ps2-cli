using RatchetPs2.Core.IO.Vif;

internal static class Ps2VifPacketTests
{
    public static void Run()
    {
        using var stream = new MemoryStream();
        Ps2VifPacket.WriteHeader(stream, 0x0101, 0, 0x01); // STCYCL has no payload.
        Ps2VifPacket.WriteHeader(stream, 0, 0, 0x20); // STMASK
        stream.Write(new byte[4]);
        Ps2VifPacket.WriteHeader(stream, 0x0123, 2, 0x69); // UNPACK_V3_16
        stream.Write(new byte[12]);
        Ps2VifPacket.WriteHeader(stream, 0x0004, 3, 0x6F); // UNPACK_V4_5
        stream.Write(new byte[8]);
        Ps2VifPacket.WriteHeader(stream, 1, 0, 0x50); // DIRECT
        stream.Write(new byte[16]);

        var packets = Ps2VifPacket.ReadSpans(stream.ToArray());
        Expect(packets.Count == 5, "VIF command stream should retain all commands.");
        Expect(packets[0].Kind == "STCYCL" && packets[0].RawPayloadLength == 0
            && packets[0].TotalLength == 4, "STCYCL should not consume a payload.");
        Expect(packets[1].Kind == "STMASK" && packets[1].RawPayloadLength == 4,
            "STMASK should consume one word.");
        Expect(packets[2].Kind == "UNPACK_V3_16" && packets[2].RawPayloadLength == 12
            && packets[2].DestinationAddress == 0x123,
            "V3_16 metadata should be decoded from the shared command description.");
        Expect(packets[3].Kind == "UNPACK_V4_5_PACKED" && packets[3].RawPayloadLength == 6
            && packets[3].AlignedPayloadLength == 8,
            "V4_5 rows should use packed 16-bit values with word alignment.");
        Expect(packets[4].Kind == "DIRECT" && packets[4].RawPayloadLength == 16,
            "DIRECT payload length should come from the immediate field.");

        Expect(Ps2VifPacket.TryGetUnpackPayloadLength(0x6E, 0, out var fullV4BytePayload)
            && fullV4BytePayload == 1024,
            "An UNPACK NUM field of zero should represent 256 rows.");
        Expect(!Ps2VifPacket.TryGetUnpackPayloadLength(0x63, 1, out _),
            "Reserved five-bit UNPACK formats should not be treated as valid payloads.");

        var fillCycle = new Ps2VifCycle(CycleLength: 2, WriteLength: 4);
        Expect(Ps2VifPacket.GetUnpackSourceElementCount(10, fillCycle) == 6,
            "STCYCL fill mode should only consume CL source elements per WL output elements.");
        Expect(Ps2VifPacket.GetUnpackSourceElementCount(10, new(4, 2)) == 10,
            "STCYCL skip mode should consume one source element per output element.");

        using var fillStream = new MemoryStream();
        fillStream.Write(Ps2VifPacket.BuildBytes(fillCycle.Immediate, 0, 0x01, []));
        fillStream.Write(Ps2VifPacket.BuildBytes(0x0123, 10, 0xEE, new byte[24], fillCycle));
        fillStream.Write(Ps2VifPacket.BuildBytes(0, 0, 0x00, []));
        var fillPackets = Ps2VifPacket.ReadSpans(fillStream.ToArray());
        Expect(fillPackets.Count == 3
            && fillPackets[1].RawPayloadLength == 24
            && fillPackets[1].Cycle == fillCycle
            && fillPackets[2].Offset == 32,
            "Shared parsing should retain STCYCL state when locating the next packet.");

        var replaced = Ps2VifPacket.ReplacePacket(
            fillStream.ToArray(),
            fillPackets[1],
            num: 14,
            new byte[32]);
        var replacedPackets = Ps2VifPacket.ReadSpans(replaced);
        Expect(replacedPackets.Count == 3
            && replacedPackets[1].Immediate == fillPackets[1].Immediate
            && replacedPackets[1].CommandByte == fillPackets[1].CommandByte
            && replacedPackets[1].RawPayloadLength == 32
            && replacedPackets[2].Offset == 40,
            "Template replacement should preserve command metadata and resize the surrounding stream.");

        ExpectThrows<InvalidDataException>(
            () => Ps2VifPacket.BuildBytes(0x0123, 10, 0x6E, new byte[40], fillCycle),
            "Packet construction should reject payloads that do not match STCYCL semantics.");
        ExpectThrows<InvalidDataException>(
            () => Ps2VifPacket.BuildBytes(0, 1, 0x63, []),
            "Packet construction should reject reserved UNPACK formats.");
        ExpectThrows<ArgumentException>(
            () => Ps2VifPacket.ReplacePacket(
                fillStream.ToArray(),
                fillPackets[1] with { Immediate = 0x0124 },
                num: 10,
                new byte[24]),
            "Packet replacement should reject a template from a different stream.");

        Console.WriteLine("Shared PS2 VIF codec tests passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
