namespace RatchetPs2.Core.Wad.Models;

public sealed record WadDecompressionOptions(
    int MaxOutputBytes = 512 * 1024 * 1024,
    int MaxExpansionRatio = 1024);

public sealed record WadCompressionResult(
    byte[] CompressedBytes,
    int UncompressedSize,
    int CompressedSize,
    string UncompressedSha256,
    string CompressedSha256);
