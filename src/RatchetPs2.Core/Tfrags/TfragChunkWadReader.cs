using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Core.Tfrags;

public static class TfragChunkWadReader
{
    private const int HeaderSize = 0x10;

    public static byte[] ReadTerrainPayload(ReadOnlySpan<byte> chunkBytes)
    {
        if (!TryGetPayload(chunkBytes, 0x00, out var payload))
        {
            return [];
        }

        return BinaryMagic.IsWad(payload)
            ? WadCompression.Decompress(payload)
            : payload.ToArray();
    }

    public static byte[] ReadCollisionPayload(ReadOnlySpan<byte> chunkBytes)
    {
        if (!TryGetPayload(chunkBytes, 0x04, out var payload))
        {
            return [];
        }

        return BinaryMagic.IsWad(payload)
            ? WadCompression.Decompress(payload)
            : payload.ToArray();
    }

    private static bool TryGetPayload(
        ReadOnlySpan<byte> chunkBytes,
        int headerOffset,
        out ReadOnlySpan<byte> payload)
    {
        payload = [];
        if (chunkBytes.Length < HeaderSize)
        {
            return false;
        }

        var payloadOffset = BinarySpanReader.ReadInt32LittleEndian(chunkBytes, headerOffset);
        if (payloadOffset < HeaderSize || payloadOffset >= chunkBytes.Length)
        {
            return false;
        }

        var payloadEnd = chunkBytes.Length;
        for (var offset = 0; offset < HeaderSize; offset += sizeof(int))
        {
            var candidate = BinarySpanReader.ReadInt32LittleEndian(chunkBytes, offset);
            if (candidate > payloadOffset && candidate <= chunkBytes.Length)
            {
                payloadEnd = Math.Min(payloadEnd, candidate);
            }
        }

        payload = chunkBytes[payloadOffset..payloadEnd];
        return payload.Length > 0;
    }
}
