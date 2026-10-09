using RatchetPs2.Core.Fx;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Games.UYA.Fx;

public static class UyaFxTextureComposer
{
    public static FxTextureComposition Compose(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> storedAssetBytes,
        IReadOnlyList<FxTextureReplacement> replacements,
        IReadOnlyList<FxIndexedTexture> additions,
        FxTextureCompositionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (replacements.Count == 0 && additions.Count == 0)
            return FxTextureComposer.Compose(
                headerBytes, storedAssetBytes, replacements, additions, options, cancellationToken);
        if (storedAssetBytes.Length > options.MaximumStoredAssetBytes)
            throw new InvalidDataException("Stored FX asset payload exceeds the configured size limit.");

        var stored = storedAssetBytes.ToArray();
        var wasCompressed = BinaryMagic.IsWad(stored);
        var sourceAsset = wasCompressed
            ? WadCompression.Decompress(
                stored,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken)
            : stored;
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var fxDataEnd = UyaLevelAssetComposer.FindPayloadEnd(
            headerBytes, sourceAsset, header.FxTextureDataOffset);

        var fxComposition = FxTextureComposer.Compose(
            headerBytes,
            sourceAsset.AsSpan(0, fxDataEnd),
            replacements,
            additions,
            options,
            cancellationToken);
        var fxPayload = fxComposition.AssetBytes.AsMemory(header.FxTextureDataOffset);
        var relocated = UyaLevelAssetComposer.ComposeAssetWad(
            fxComposition.HeaderBytes,
            sourceAsset,
            new(Fx: fxPayload),
            cancellationToken);
        if (relocated.AssetWadBytes.Length > options.MaximumDecompressedAssetBytes)
            throw new InvalidDataException("Composed FX asset payload exceeds the configured decompressed-size limit.");
        var outputAsset = wasCompressed
            ? WadCompression.CompressVerified(
                relocated.AssetWadBytes,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken).CompressedBytes
            : relocated.AssetWadBytes;
        if (outputAsset.Length > options.MaximumStoredAssetBytes)
            throw new InvalidDataException("Composed FX asset payload exceeds the configured stored-size limit.");
        return new(relocated.HeaderBytes, outputAsset, false);
    }
}
