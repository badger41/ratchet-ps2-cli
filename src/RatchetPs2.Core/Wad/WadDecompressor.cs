using System.Buffers.Binary;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Wad;

internal static class WadDecompressor
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

    public static byte[] Decompress(Stream stream) => Decompress(stream, new(), default);

    public static byte[] Decompress(
        Stream stream,
        WadDecompressionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be readable and seekable.", nameof(stream));
        }
        if (stream.Length > int.MaxValue)
            throw new InvalidDataException("The compressed WAD stream is too large to read in memory.");

        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        return Decompress(stream.ReadBytesExactly((int)stream.Length), options, cancellationToken);
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source) => Decompress(source, new(), default);

    public static byte[] Decompress(
        ReadOnlySpan<byte> source,
        WadDecompressionOptions options,
        CancellationToken cancellationToken)
    {
        WadCompression.ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length < HeaderSize)
        {
            throw new InvalidDataException("Input is too small to contain a valid WAD header.");
        }

        ValidateWadMagic(source);

        var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(3, sizeof(int)));
        if (compressedSize < HeaderSize || compressedSize > source.Length)
        {
            throw new InvalidDataException("Compressed WAD size is invalid.");
        }

        var end = compressedSize;
        var cursor = HeaderSize;
        var payloadStart = HeaderSize;
        var payloadLength = compressedSize - HeaderSize;
        var ratioLimit = Math.Min(int.MaxValue, (long)payloadLength * options.MaxExpansionRatio);
        var outputLimit = (int)Math.Min(options.MaxOutputBytes, ratioLimit);
        var destination = new List<byte>(Math.Min(compressedSize, outputLimit));

        while (cursor < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecompressPacket(destination, source, ref cursor, payloadStart, end, outputLimit, cancellationToken);
        }

        return destination.ToArray();
    }

    public static byte[] Decompress(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Decompress(source.AsSpan(), new(), default);
    }

    public static byte[] Decompress(
        byte[] source,
        WadDecompressionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Decompress(source.AsSpan(), options, cancellationToken);
    }

    private static void ValidateWadMagic(ReadOnlySpan<byte> source)
    {
        var headerMagic = source[..WadMagic.Length];
        if (!headerMagic.SequenceEqual(WadMagic))
        {
            throw new InvalidDataException("Input is not a valid compressed WAD file because it does not start with WAD magic.");
        }
    }

    private static void DecompressPacket(
        List<byte> destination,
        ReadOnlySpan<byte> source,
        ref int cursor,
        int payloadStart,
        int end,
        int outputLimit,
        CancellationToken cancellationToken)
    {
        var packetFlag = Read8(source, ref cursor, payloadStart, end);

        if (packetFlag < LiteralPacketMaxFlag)
        {
            HandleLiteralPacket(destination, source, ref cursor, payloadStart, end, packetFlag, outputLimit);
            return;
        }

        var matchLength = 0;
        var lookbackOffset = -1;

        if (packetFlag < FarMatchPacketMaxFlag)
        {
            if (TryHandleFarMatchPacket(destination, source, ref cursor, payloadStart, end, packetFlag,
                    cancellationToken, out lookbackOffset, out matchLength))
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

        CopyMatch(destination, lookbackOffset, matchLength, outputLimit);

        var littleLiteralSize = source[cursor - 2] & 0b11;
        CopyLiteral(destination, source, ref cursor, payloadStart, end, littleLiteralSize, outputLimit);
    }

    private static byte Read8(ReadOnlySpan<byte> source, ref int cursor, int payloadStart, int end)
    {
        if (cursor >= end || cursor < payloadStart)
        {
            throw new InvalidDataException("Unexpected end of compressed WAD buffer.");
        }

        return source[cursor++];
    }

    private static void CopyLiteral(
        List<byte> destination,
        ReadOnlySpan<byte> source,
        ref int cursor,
        int payloadStart,
        int end,
        int size,
        int outputLimit)
    {
        if (cursor + size > end || cursor < payloadStart)
        {
            throw new InvalidDataException("Unexpected end of compressed WAD buffer.");
        }

        EnsureOutputCapacity(destination, size, outputLimit);
        for (var i = 0; i < size; i++)
        {
            destination.Add(source[cursor + i]);
        }

        cursor += size;
    }

    private static void HandleLiteralPacket(
        List<byte> destination,
        ReadOnlySpan<byte> source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag,
        int outputLimit)
    {
        var literalLength = packetFlag != 0
            ? packetFlag + SmallLiteralBaseLength
            : Read8(source, ref cursor, payloadStart, end) + LargeLiteralBaseLength;

        CopyLiteral(destination, source, ref cursor, payloadStart, end, literalLength, outputLimit);

        if (cursor < end && source[cursor] < LiteralPacketMaxFlag)
        {
            throw new InvalidDataException("Unexpected double literal in compressed WAD stream.");
        }
    }

    private static bool TryHandleFarMatchPacket(
        List<byte> destination,
        ReadOnlySpan<byte> source,
        ref int cursor,
        int payloadStart,
        int end,
        byte packetFlag,
        CancellationToken cancellationToken,
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

        AlignCursorToNextCompressedBlockBoundary(ref cursor, payloadStart, end, cancellationToken);
        return true;
    }

    private static void HandleMediumMatchPacket(List<byte> destination, ReadOnlySpan<byte> source, ref int cursor, int payloadStart, int end, byte packetFlag, out int lookbackOffset, out int matchLength)
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

    private static void HandleSmallMatchPacket(List<byte> destination, ReadOnlySpan<byte> source, ref int cursor, int payloadStart, int end, byte packetFlag, out int lookbackOffset, out int matchLength)
    {
        var majorLookbackByte = Read8(source, ref cursor, payloadStart, end);
        lookbackOffset = destination.Count - majorLookbackByte * SmallMatchLookbackStrideBytes - ((packetFlag >> 2) & 0b111) - 1;
        matchLength = (packetFlag >> 5) + 1;
    }

    private static void CopyMatch(List<byte> destination, int lookbackOffset, int matchLength, int outputLimit)
    {
        if (matchLength == 1)
        {
            return;
        }

        if (lookbackOffset < 0 || lookbackOffset >= destination.Count)
        {
            throw new InvalidDataException("Match packet points outside of the decompressed buffer.");
        }

        EnsureOutputCapacity(destination, matchLength, outputLimit);
        for (var i = 0; i < matchLength; i++)
        {
            destination.Add(destination[lookbackOffset + i]);
        }
    }

    private static void AlignCursorToNextCompressedBlockBoundary(
        ref int cursor,
        int payloadStart,
        int end,
        CancellationToken cancellationToken)
    {
        while (((cursor - payloadStart) % CompressedBlockAlignmentBytes) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cursor++;
            if (cursor > end)
            {
                throw new InvalidDataException("Compressed WAD padding stepped outside the buffer.");
            }
        }
    }

    private static void EnsureOutputCapacity(List<byte> destination, int additionalBytes, int outputLimit)
    {
        if ((long)destination.Count + additionalBytes > outputLimit)
            throw new InvalidDataException(
                $"Decompressed WAD exceeds the configured 0x{outputLimit:X}-byte output or expansion limit.");
    }
}
