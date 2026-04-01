using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Wad;

public static class WadCompression
{
    private const int HeaderSize = 0x10;
    private const byte LiteralPacketMaxFlag = 0x10;
    private const byte FarMatchPacketMaxFlag = 0x20;
    private const byte MediumMatchPacketMaxFlag = 0x40;
    private const int SmallLiteralBaseLength = 3;
    private const int LargeLiteralBaseLength = 18;
    private const int FarMatchExtendedLengthBase = 7;
    private const int MatchCopyLengthAdjustment = 2;
    private const int MediumMatchExtendedLengthBase = 0x1f;
    private const int MediumMatchLengthAdjustment = 2;
    private const int SmallMatchLookbackStrideBytes = 8;
    private const int MatchLookbackHighByteStrideBytes = 0x40;
    private const int FarMatchLookbackPageStrideBytes = 0x800;
    private const int FarMatchLookbackWindowBiasBytes = 0x4000;
    private const int CompressedBlockAlignmentBytes = 0x1000;
    private static ReadOnlySpan<byte> WadMagic => "WAD"u8;

    public static byte[] Decompress(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be readable and seekable.", nameof(stream));
        }

        stream.Position = 0;
        return Decompress(stream.ReadBytesExactly((int)stream.Length));
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source)
    {
        var buffer = source.ToArray();

        return Decompress(buffer);
    }

    public static byte[] Decompress(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length < HeaderSize)
        {
            throw new InvalidDataException("Input is too small to contain a valid WAD header.");
        }

        ValidateWadMagic(source);

        var compressedSize = BitConverter.ToInt32(source[3..7]);
        if (compressedSize <= 0 || compressedSize > source.Length)
        {
            throw new InvalidDataException("Compressed WAD size is invalid.");
        }

        var end = compressedSize;
        var cursor = HeaderSize;
        var payloadStart = HeaderSize;
        var destination = new List<byte>(compressedSize * 2);

        while (cursor < end)
        {
            DecompressPacket(destination, source, ref cursor, payloadStart, end);
        }

        return destination.ToArray();
    }

    private static void ValidateWadMagic(ReadOnlySpan<byte> source)
    {
        var headerMagic = source[..WadMagic.Length];
        var hasValidMagic = headerMagic.SequenceEqual(WadMagic);

        if (!hasValidMagic)
        {
            throw new InvalidDataException("Input is not a valid compressed WAD file because it does not start with WAD magic.");
        }
    }

    private static void DecompressPacket(List<byte> destination, byte[] source, ref int cursor, int payloadStart, int end)
    {
        // Packet families match the game's FastDecompress routine:
        //   < 0x10  => literal packet
        //   < 0x20  => far match packet
        //   < 0x40  => medium match packet
        //   >= 0x40 => small match packet
        var packetFlag = Read8(source, ref cursor, payloadStart, end);

        if (packetFlag < LiteralPacketMaxFlag)
        {
            HandleLiteralPacket(destination, source, ref cursor, payloadStart, end, packetFlag);
            return;
        }

        var matchLength = 0;
        var lookbackOffset = -1;

        if (packetFlag < FarMatchPacketMaxFlag)
        {
            if (TryHandleFarMatchPacket(
                    destination,
                    source,
                    ref cursor,
                    payloadStart,
                    end,
                    packetFlag,
                    out lookbackOffset,
                    out matchLength))
            {
                return;
            }
        }
        else if (packetFlag < MediumMatchPacketMaxFlag)
        {
            HandleMediumMatchPacket(destination, source, ref cursor, payloadStart, end, packetFlag, out lookbackOffset, out matchLength);
        }
        else
        {
            HandleSmallMatchPacket(destination, source, ref cursor, payloadStart, end, packetFlag, out lookbackOffset, out matchLength);
        }

        CopyMatch(destination, lookbackOffset, matchLength);

        var littleLiteralSize = source[cursor - 2] & 3;
        CopyLiteral(destination, source, ref cursor, payloadStart, end, littleLiteralSize);
    }

    private static byte Read8(byte[] source, ref int cursor, int payloadStart, int end)
    {
        if (cursor >= end || cursor < payloadStart)
        {
            throw new InvalidDataException("Unexpected end of compressed WAD buffer.");
        }

        return source[cursor++];
    }

    private static void CopyLiteral(List<byte> destination, byte[] source, ref int cursor, int payloadStart, int end, int size)
    {
        if (cursor + size > end || cursor < payloadStart)
        {
            throw new InvalidDataException("Unexpected end of compressed WAD buffer.");
        }

        for (var i = 0; i < size; i++)
        {
            destination.Add(source[cursor + i]);
        }

        cursor += size;
    }

    private static void HandleLiteralPacket(
        List<byte> destination,
        byte[] source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag)
    {
        var literalLength = packetFlag != 0
            ? packetFlag + SmallLiteralBaseLength
            : Read8(source, ref cursor, payloadStart, end) + LargeLiteralBaseLength;

        CopyLiteral(destination, source, ref cursor, payloadStart, end, literalLength);

        if (cursor < end && source[cursor] < LiteralPacketMaxFlag)
        {
            throw new InvalidDataException("Unexpected double literal in compressed WAD stream.");
        }
    }

    private static bool TryHandleFarMatchPacket(
        List<byte> destination,
        byte[] source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag,
        out int lookbackOffset,
        out int matchLength)
    {
        matchLength = packetFlag & 0b111;
        if (matchLength == 0)
        {
            matchLength = Read8(source, ref cursor, payloadStart, end) + FarMatchExtendedLengthBase;
        }

        var lowOffsetByte = Read8(source, ref cursor, payloadStart, end);
        var highOffsetByte = Read8(source, ref cursor, payloadStart, end);

        lookbackOffset = destination.Count
            - ((packetFlag & 0b1000) * FarMatchLookbackPageStrideBytes)
            - (highOffsetByte * MatchLookbackHighByteStrideBytes)
            - (lowOffsetByte >> 2);

        if (lookbackOffset != destination.Count)
        {
            matchLength += MatchCopyLengthAdjustment;
            lookbackOffset -= FarMatchLookbackWindowBiasBytes;
            return false;
        }

        if (matchLength == 1)
        {
            return false;
        }

        AlignCursorToNextCompressedBlockBoundary(ref cursor, payloadStart, end);
        return true;
    }

    private static void HandleMediumMatchPacket(
        List<byte> destination,
        byte[] source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag,
        out int lookbackOffset,
        out int matchLength)
    {
        matchLength = packetFlag & 0x1f;
        if (matchLength == 0)
        {
            matchLength = Read8(source, ref cursor, payloadStart, end) + MediumMatchExtendedLengthBase;
        }

        matchLength += MediumMatchLengthAdjustment;

        var lowOffsetBits = Read8(source, ref cursor, payloadStart, end);
        var highOffsetBits = Read8(source, ref cursor, payloadStart, end);
        lookbackOffset = destination.Count - (highOffsetBits * MatchLookbackHighByteStrideBytes) - (lowOffsetBits >> 2) - 1;
    }

    private static void HandleSmallMatchPacket(
        List<byte> destination,
        byte[] source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag,
        out int lookbackOffset,
        out int matchLength)
    {
        var majorLookbackByte = Read8(source, ref cursor, payloadStart, end);
        lookbackOffset = destination.Count - majorLookbackByte * SmallMatchLookbackStrideBytes - ((packetFlag >> 2) & 0b111) - 1;
        matchLength = (packetFlag >> 5) + 1;
    }

    private static void CopyMatch(List<byte> destination, int lookbackOffset, int matchLength)
    {
        if (matchLength == 1)
        {
            return;
        }

        if (lookbackOffset < 0 || lookbackOffset >= destination.Count)
        {
            throw new InvalidDataException("Match packet points outside of the decompressed buffer.");
        }

        for (var i = 0; i < matchLength; i++)
        {
            destination.Add(destination[lookbackOffset + i]);
        }
    }

    private static void AlignCursorToNextCompressedBlockBoundary(ref int cursor, int payloadStart, int end)
    {
        while (((cursor - payloadStart) % CompressedBlockAlignmentBytes) != 0)
        {
            cursor++;
            if (cursor > end)
            {
                throw new InvalidDataException("Compressed WAD padding stepped outside the buffer.");
            }
        }
    }
}