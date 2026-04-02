using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Wad;

internal static class WadCompressor
{
    private const int HeaderSize = 0x10;
    private const int DefaultThreadCount = 1;
    private const byte LiteralPacketMaxFlag = 0x10;
    private const byte FarMatchPacketMaxFlag = 0x20;
    private const byte MediumMatchPacketMaxFlag = 0x40;
    private const int SmallLiteralBaseLength = 3;
    private const int LargeLiteralBaseLength = 18;
    private const int MatchCopyLengthAdjustment = 2;
    private const int MaxMatchLength = 264;
    private const int MaxLiteralLength = 273;
    private const int MaxSmallMatchLength = 8;
    private const int MaxMediumMatchLength = 33;
    private const int MaxMediumFarMatchLength = 9;
    private const int MaxSmallMatchLookback = 2048;
    private const int MaxBigMatchLookback = 16384;
    private const int MaxFarMatchLookbackWithoutPageFlag = 32704;
    private const int HashWindowSize = 32768;
    private const int HashWindowMask = HashWindowSize - 1;
    private const int DoNotInjectLiteralFlag = 0x100;
    private const int CompressedChunkSizeBytes = 0x2000;
    private const int CompressedChunkPayloadSizeBytes = 0x1ff0;
    private const int PaddingPacketSizeBytes = 3;
    private const int SmallMatchLookbackStrideBytes = 8;
    private const int MatchLookbackHighByteStrideBytes = 0x40;
    private static ReadOnlySpan<byte> WadMagic => "WAD"u8;
    private static ReadOnlySpan<byte> DefaultHeaderTag => "RACCLI001"u8;

    private sealed class MatchResult
    {
        public int LiteralLength { get; set; }
        public int MatchOffset { get; set; }
        public int MatchLength { get; set; }
    }

    public static byte[] Compress(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be readable and seekable.", nameof(stream));
        }

        stream.Position = 0;
        return Compress(stream.ReadBytesExactly((int)stream.Length));
    }

    public static byte[] Compress(ReadOnlySpan<byte> source) => Compress(source.ToArray());

    public static byte[] Compress(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var destination = new List<byte>(source.Length / 2 + HeaderSize);
        Compress(destination, source, DefaultHeaderTag, DefaultThreadCount);
        return destination.ToArray();
    }

    private static void Compress(List<byte> destination, byte[] source, ReadOnlySpan<byte> muffin, int threadCount)
    {
        var intermediateBuffers = new List<byte>[threadCount];
        for (var index = 0; index < threadCount; index++)
        {
            intermediateBuffers[index] = new List<byte>();
        }

        CompressIntermediate(intermediateBuffers[0], source, 0, source.Length);

        var headerOffset = destination.Count;
        WriteCompressionHeader(destination, muffin);

        foreach (var intermediate in intermediateBuffers)
        {
            AppendCompressedBuffer(destination, intermediate, headerOffset);
        }

        WriteInt32LittleEndian(destination, headerOffset + 3, destination.Count - headerOffset);
    }

    private static void CompressIntermediate(List<byte> destination, byte[] source, int sourceOffset, int sourceEnd)
    {
        var lastFlag = DoNotInjectLiteralFlag;
        var hashTable = Enumerable.Repeat(-HashWindowSize, HashWindowSize).ToArray();
        var chain = Enumerable.Repeat(-HashWindowSize, HashWindowSize).ToArray();

        while (sourceOffset < sourceEnd)
        {
            var match = sourceOffset + MaxMatchLength >= sourceEnd
                ? FindMatch(source, sourceOffset, sourceEnd, hashTable, chain, endOfBuffer: true)
                : FindMatch(source, sourceOffset, sourceEnd, hashTable, chain, endOfBuffer: false);

            if (match.LiteralLength > 0)
            {
                EncodeLiteralPacket(destination, source, ref sourceOffset, ref lastFlag, match.LiteralLength);
            }

            if (match.MatchLength > 0)
            {
                EncodeMatchPacket(destination, ref sourceOffset, ref lastFlag, match.MatchOffset, match.MatchLength);
            }
        }
    }

    private static MatchResult FindMatch(byte[] source, int sourceOffset, int sourceEnd, int[] hashTable, int[] chain, bool endOfBuffer)
    {
        var maxLiteralLength = endOfBuffer ? Math.Min(MaxLiteralLength, sourceEnd - sourceOffset) : MaxLiteralLength;
        var bestMatch = new MatchResult { LiteralLength = maxLiteralLength };

        for (var literalIndex = 0; literalIndex < maxLiteralLength; literalIndex++)
        {
            var target = sourceOffset + literalIndex;
            var maxMatchLength = endOfBuffer ? Math.Min(MaxMatchLength, sourceEnd - sourceOffset - literalIndex) : MaxMatchLength;
            var hashKey = Hash32(source[target] | (source[target + 1] << 8) | (source[target + 2] << 16)) & HashWindowMask;
            var next = hashTable[hashKey];
            var minimumOffset = target - MaxFarMatchLookbackWithoutPageFlag;
            var hits = 0;

            while (next > minimumOffset && ++hits < 16)
            {
                if (!endOfBuffer && BitConverter.ToUInt16(source, next) != BitConverter.ToUInt16(source, target))
                {
                    next = chain[next & HashWindowMask];
                    continue;
                }

                var matchedBytes = endOfBuffer ? 0 : 2;
                while (matchedBytes < maxMatchLength && source[target + matchedBytes] == source[next + matchedBytes])
                {
                    matchedBytes++;
                }

                if (matchedBytes > bestMatch.MatchLength)
                {
                    bestMatch.MatchLength = matchedBytes;
                    bestMatch.MatchOffset = next;
                }

                next = chain[next & HashWindowMask];
            }

            chain[target & HashWindowMask] = hashTable[hashKey];
            hashTable[hashKey] = target;

            if (bestMatch.MatchLength >= 3)
            {
                bestMatch.LiteralLength = literalIndex;
                break;
            }
        }

        if (bestMatch.MatchLength < 3)
        {
            bestMatch.MatchOffset = 0;
            bestMatch.MatchLength = 0;
        }

        return bestMatch;
    }

    private static void EncodeMatchPacket(List<byte> destination, ref int sourceOffset, ref int lastFlag, int matchOffset, int matchLength)
    {
        var packetStart = destination.Count;
        var lookbackDistance = sourceOffset - matchOffset;

        if (matchLength <= MaxSmallMatchLength && lookbackDistance <= MaxSmallMatchLookback)
        {
            var lowBits = (lookbackDistance - 1) % SmallMatchLookbackStrideBytes;
            var highBits = (lookbackDistance - 1) / SmallMatchLookbackStrideBytes;
            destination.Add((byte)(((matchLength - 1) << 5) | (lowBits << 2)));
            destination.Add((byte)highBits);
        }
        else if (lookbackDistance <= MaxBigMatchLookback)
        {
            if (matchLength > MaxMediumMatchLength)
            {
                destination.Add(0b0010_0000);
                destination.Add((byte)(matchLength - MaxMediumMatchLength));
            }
            else
            {
                destination.Add((byte)(0b0010_0000 | (matchLength - MatchCopyLengthAdjustment)));
            }

            var lowBits = (lookbackDistance - 1) % MatchLookbackHighByteStrideBytes;
            var highBits = (lookbackDistance - 1) / MatchLookbackHighByteStrideBytes;
            destination.Add((byte)(lowBits << 2));
            destination.Add((byte)highBits);
        }
        else
        {
            var usePageFlag = lookbackDistance > MaxFarMatchLookbackWithoutPageFlag;
            var lookbackBase = usePageFlag ? 0x4800 : 0x4000;
            var lowBits = (lookbackDistance - lookbackBase) % MatchLookbackHighByteStrideBytes;
            var highBits = (lookbackDistance - lookbackBase) / MatchLookbackHighByteStrideBytes;

            if (matchLength > MaxMediumFarMatchLength)
            {
                destination.Add((byte)(0b0001_0000 | ((usePageFlag ? 1 : 0) << 3)));
                destination.Add((byte)(matchLength - MaxMediumFarMatchLength));
            }
            else
            {
                destination.Add((byte)(0b0001_0000 | ((usePageFlag ? 1 : 0) << 3) | (matchLength - MatchCopyLengthAdjustment)));
            }

            destination.Add((byte)(lowBits << 2));
            destination.Add((byte)highBits);
        }

        sourceOffset += matchLength;
        lastFlag = destination[packetStart];
    }

    private static void EncodeLiteralPacket(List<byte> destination, byte[] source, ref int sourceOffset, ref int lastFlag, int literalLength)
    {
        var packetStart = destination.Count;

        if (lastFlag < LiteralPacketMaxFlag)
        {
            lastFlag = 0x11;
            destination.AddRange([0x11, 0x00, 0x00]);
            packetStart = destination.Count;
        }

        if (literalLength <= 3)
        {
            if (lastFlag == DoNotInjectLiteralFlag)
            {
                lastFlag = 0x11;
                destination.AddRange([0x11, 0x00, 0x00]);
                packetStart = destination.Count;
            }

            destination[packetStart - 2] |= (byte)literalLength;
            destination.AddRange(source.AsSpan(sourceOffset, literalLength).ToArray());
            sourceOffset += literalLength;
            lastFlag = DoNotInjectLiteralFlag;
            return;
        }

        if (literalLength <= LargeLiteralBaseLength)
        {
            destination.Add((byte)(literalLength - SmallLiteralBaseLength));
        }
        else
        {
            destination.Add(0);
            destination.Add((byte)(literalLength - LargeLiteralBaseLength));
        }

        destination.AddRange(source.AsSpan(sourceOffset, literalLength).ToArray());
        sourceOffset += literalLength;
        lastFlag = destination[packetStart];
    }

    private static void AppendCompressedBuffer(List<byte> destination, List<byte> intermediate, int headerOffset)
    {
        for (var position = 0; position < intermediate.Count;)
        {
            var packetSize = GetPacketSize(intermediate, position);
            var insertDummyPacket = destination.Count != headerOffset + HeaderSize && position == 0;
            var insertSize = packetSize + (insertDummyPacket ? PaddingPacketSizeBytes : 0);

            if ((((destination.Count - headerOffset) + CompressedChunkPayloadSizeBytes) % CompressedChunkSizeBytes) + insertSize > CompressedChunkSizeBytes - PaddingPacketSizeBytes)
            {
                destination.AddRange([0x12, 0x00, 0x00]);
                while ((destination.Count - headerOffset) % CompressedChunkSizeBytes != HeaderSize)
                {
                    destination.Add(0xEE);
                }
            }

            if (insertDummyPacket)
            {
                destination.AddRange([0x11, 0x00, 0x00]);
            }

            destination.AddRange(intermediate.GetRange(position, packetSize));
            position += packetSize;
        }
    }

    private static int GetPacketSize(List<byte> source, int position)
    {
        var packetSize = 1;
        var packetFlag = source[position];

        if (packetFlag < LiteralPacketMaxFlag)
        {
            packetSize += packetFlag != 0 ? packetFlag + SmallLiteralBaseLength : 1 + (source[position + 1] + LargeLiteralBaseLength);

            if (position + packetSize < source.Count && source[position + packetSize] < LiteralPacketMaxFlag)
            {
                throw new InvalidDataException("Compression failed because the intermediate buffer contains consecutive literal packets.");
            }

            return packetSize;
        }

        if (packetFlag < FarMatchPacketMaxFlag)
        {
            if ((packetFlag & 0b111) == 0)
            {
                packetSize++;
            }

            packetSize += 2;
        }
        else if (packetFlag < MediumMatchPacketMaxFlag)
        {
            if ((packetFlag & 0x1f) == 0)
            {
                packetSize++;
            }

            packetSize += 2;
        }
        else
        {
            packetSize++;
        }

        packetSize += source[position + packetSize - 2] & 0b11;
        return packetSize;
    }

    private static int Hash32(int value) => ((value * 12) + value) >> 3;

    private static void WriteCompressionHeader(List<byte> destination, ReadOnlySpan<byte> muffin)
    {
        var header = new byte[HeaderSize];
        WadMagic.CopyTo(header);
        var muffinBytes = muffin.Length > 0 ? muffin : DefaultHeaderTag;
        muffinBytes[..Math.Min(9, muffinBytes.Length)].CopyTo(header.AsSpan(7));
        destination.AddRange(header);
    }

    private static void WriteInt32LittleEndian(List<byte> destination, int offset, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var index = 0; index < bytes.Length; index++)
        {
            destination[offset + index] = bytes[index];
        }
    }
}