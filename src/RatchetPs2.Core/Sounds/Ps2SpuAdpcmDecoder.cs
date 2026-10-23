namespace RatchetPs2.Core.Sounds;

internal static class Ps2SpuAdpcmDecoder
{
    private static readonly int[,] Coefficients =
    {
        { 0, 0 },
        { 60, 0 },
        { 115, -52 },
        { 98, -55 },
        { 122, -60 },
    };

    public static short[] Decode(
        ReadOnlySpan<byte> encoded,
        int decodedFrameCount,
        CancellationToken cancellationToken)
    {
        if (encoded.Length % 16 != 0 || decodedFrameCount != encoded.Length / 16 * 28)
            throw new InvalidDataException("PS2 SPU ADPCM decode bounds are inconsistent.");
        var output = new short[decodedFrameCount];
        var history1 = 0;
        var history2 = 0;
        var outputIndex = 0;
        for (var offset = 0; offset < encoded.Length; offset += 16)
        {
            if ((offset & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var predictor = encoded[offset] >> 4;
            var shift = encoded[offset] & 0x0f;
            if (predictor > 4 || shift > 12)
                throw new InvalidDataException("PS2 SPU ADPCM sample has an invalid frame header.");
            for (var index = 0; index < 28; index++)
            {
                var packed = encoded[offset + 2 + index / 2];
                var nibble = (index & 1) == 0 ? packed & 0x0f : packed >> 4;
                if (nibble >= 8) nibble -= 16;
                var value = (nibble << 12) >> shift;
                value += (history1 * Coefficients[predictor, 0]
                    + history2 * Coefficients[predictor, 1] + 32) >> 6;
                value = Math.Clamp(value, short.MinValue, short.MaxValue);
                output[outputIndex++] = (short)value;
                history2 = history1;
                history1 = value;
            }
        }
        return output;
    }
}
