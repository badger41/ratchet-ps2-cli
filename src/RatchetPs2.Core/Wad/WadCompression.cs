namespace RatchetPs2.Core.Wad;

public static class WadCompression
{
    public static byte[] Decompress(Stream stream) => WadDecompressor.Decompress(stream);

    public static byte[] Decompress(ReadOnlySpan<byte> source) => WadDecompressor.Decompress(source);

    public static byte[] Decompress(byte[] source) => WadDecompressor.Decompress(source);

    public static byte[] Compress(Stream stream) => WadCompressor.Compress(stream);

    public static byte[] Compress(ReadOnlySpan<byte> source) => WadCompressor.Compress(source);

    public static byte[] Compress(byte[] source) => WadCompressor.Compress(source);
}