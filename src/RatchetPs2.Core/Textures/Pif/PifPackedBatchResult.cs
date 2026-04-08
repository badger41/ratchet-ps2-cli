namespace RatchetPs2.Core.Textures.Pif;

public sealed record PifPackedBatchResult(
    byte[] PackedPngBytes,
    int[] PngOffsets,
    int[] PngLengths);