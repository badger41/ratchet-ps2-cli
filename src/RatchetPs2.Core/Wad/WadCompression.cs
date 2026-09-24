using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Wad;

public static class WadCompression
{
    public static byte[] Decompress(Stream stream) => WadDecompressor.Decompress(stream);

    public static byte[] Decompress(
        Stream stream,
        WadDecompressionOptions options,
        CancellationToken cancellationToken = default) =>
        WadDecompressor.Decompress(stream, options, cancellationToken);

    public static byte[] Decompress(ReadOnlySpan<byte> source) => WadDecompressor.Decompress(source);

    public static byte[] Decompress(
        ReadOnlySpan<byte> source,
        WadDecompressionOptions options,
        CancellationToken cancellationToken = default) =>
        WadDecompressor.Decompress(source, options, cancellationToken);

    public static byte[] Decompress(byte[] source) => WadDecompressor.Decompress(source);

    public static byte[] Decompress(
        byte[] source,
        WadDecompressionOptions options,
        CancellationToken cancellationToken = default) =>
        WadDecompressor.Decompress(source, options, cancellationToken);

    public static byte[] Compress(Stream stream) => WadCompressor.Compress(stream);

    public static byte[] Compress(Stream stream, CancellationToken cancellationToken) =>
        WadCompressor.Compress(stream, cancellationToken);

    public static byte[] Compress(ReadOnlySpan<byte> source) => WadCompressor.Compress(source);

    public static byte[] Compress(ReadOnlySpan<byte> source, CancellationToken cancellationToken) =>
        WadCompressor.Compress(source, cancellationToken);

    public static byte[] Compress(byte[] source) => WadCompressor.Compress(source);

    public static byte[] Compress(byte[] source, CancellationToken cancellationToken) =>
        WadCompressor.Compress(source, cancellationToken);

    public static WadCompressionResult CompressVerified(
        ReadOnlySpan<byte> source,
        WadDecompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        ValidateOptions(options);
        if (source.Length > options.MaxOutputBytes)
            throw new InvalidDataException(
                $"Uncompressed WAD size 0x{source.Length:X} exceeds the configured 0x{options.MaxOutputBytes:X}-byte limit.");

        cancellationToken.ThrowIfCancellationRequested();
        var compressed = WadCompressor.Compress(source, cancellationToken);
        if (compressed.Length < 0x10
            || BinaryPrimitives.ReadInt32LittleEndian(compressed.AsSpan(3, sizeof(int))) != compressed.Length)
            throw new InvalidDataException("Compressed WAD header does not declare the complete generated stream.");
        var decompressed = WadDecompressor.Decompress(compressed, options, cancellationToken);
        if (!source.SequenceEqual(decompressed))
            throw new InvalidDataException("Compressed WAD verification did not reproduce the uncompressed input.");

        return new(
            compressed,
            source.Length,
            compressed.Length,
            Hash(source),
            Hash(compressed));
    }

    internal static void ValidateOptions(WadDecompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxOutputBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum WAD output size cannot be negative.");
        if (options.MaxExpansionRatio < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum WAD expansion ratio must be at least one.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
