using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

internal sealed class MobyVifPacket
{
    public int Offset { get; init; }
    public int Command { get; init; }
    public int Immediate { get; init; }
    public int Num { get; init; }
    public int Irq { get; init; }
    public bool IsUnpack { get; init; }
    public int VnVl { get; init; }
    public int ComponentCount { get; init; }
    public int ComponentByteSize { get; init; }
    public int RawPayloadSize { get; init; }
    public int AlignedPayloadSize { get; init; }
    public int DestinationAddr { get; init; }
    public string Kind { get; init; } = "UNKNOWN";
    public byte[] Payload { get; init; } = [];
}

internal static class MobyVifPacketReader
{
    public static List<MobyVifPacket> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var packets = new List<MobyVifPacket>();
        foreach (var span in Ps2VifPacket.ReadSpans(data))
        {
            var format = span.UnpackFormat;
            var payloadOffset = span.Offset + 4;
            var safePayloadSize = Math.Max(0, Math.Min(span.RawPayloadLength, data.Length - payloadOffset));

            packets.Add(new MobyVifPacket
            {
                Offset = span.Offset,
                Command = span.Command,
                Immediate = span.Immediate,
                Num = span.EffectiveNum,
                Irq = span.Irq,
                IsUnpack = span.IsUnpack,
                VnVl = span.VnVl,
                ComponentCount = format?.ComponentCount ?? 0,
                ComponentByteSize = format?.ComponentByteSize ?? 0,
                RawPayloadSize = span.RawPayloadLength,
                AlignedPayloadSize = span.AlignedPayloadLength,
                DestinationAddr = span.DestinationAddress,
                Kind = span.Kind,
                Payload = safePayloadSize == 0 ? [] : data[payloadOffset..(payloadOffset + safePayloadSize)],
            });
        }

        return packets;
    }
}
