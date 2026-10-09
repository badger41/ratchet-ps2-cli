using RatchetPs2.Core.Fx;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Fx;
using RatchetPs2.Games.UYA.Fx;

namespace RatchetPs2.Sdk;

public sealed record FxTextureCatalogOptions(
    int MaximumStoredAssetBytes = 256 * 1024 * 1024,
    int MaximumDecompressedAssetBytes = 256 * 1024 * 1024);

public sealed record FxTextureCapabilities(
    bool CanRead,
    bool CanReplace,
    bool CanAppend,
    string? AuthoringDisabledReason);

public sealed record FxTextureCatalogEntry(
    int Index,
    string Label,
    int Width,
    int Height,
    string PixelFormat,
    string PaletteFormat,
    int PaletteOffset,
    int PaletteLength,
    int PixelOffset,
    int PixelLength,
    bool IsSwizzled,
    bool IsValid,
    string? Diagnostic,
    byte[] CanonicalTextureBytes);

public sealed record FxTextureInventory(
    GameId Game,
    FxTextureCapabilities Capabilities,
    IReadOnlyList<FxTextureCatalogEntry> Entries);

public static class FxTextureCatalog
{
    private const string DlAuthoringDisabled =
        "FX texture authoring requires a writable Deadlocked project and bake pipeline.";

    public static FxTextureInventory Read(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> storedAssetBytes,
        FxTextureCatalogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (gameId is not (GameId.UYA or GameId.DL))
            throw new NotSupportedException($"FX texture inventory is not supported for {gameId}.");
        options ??= new();
        if (options.MaximumStoredAssetBytes < 0 || options.MaximumDecompressedAssetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "FX asset size limits cannot be negative.");
        if (storedAssetBytes.Length > options.MaximumStoredAssetBytes)
            throw new InvalidDataException(
                $"Stored FX asset size 0x{storedAssetBytes.Length:X} exceeds the configured limit.");

        cancellationToken.ThrowIfCancellationRequested();
        var stored = storedAssetBytes.ToArray();
        var assetBytes = BinaryMagic.IsWad(stored)
            ? WadCompression.Decompress(
                stored,
                new WadDecompressionOptions(options.MaximumDecompressedAssetBytes),
                cancellationToken)
            : stored;
        if (assetBytes.Length > options.MaximumDecompressedAssetBytes)
            throw new InvalidDataException(
                $"Decompressed FX asset size 0x{assetBytes.Length:X} exceeds the configured limit.");

        var isSwizzled = gameId == GameId.DL;
        var source = FxTextureInventoryReader.Read(headerBytes, assetBytes, cancellationToken);
        var entries = new FxTextureCatalogEntry[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = source[index];
            if (!entry.IsValid)
            {
                entries[index] = ToEntry(gameId, entry, isSwizzled, entry.Diagnostic, []);
                continue;
            }
            try
            {
                var canonical = PifWriter.Write(PifWriter.CreateIndexed8(
                    entry.Width,
                    entry.Height,
                    entry.PaletteBytes,
                    entry.PixelBytes,
                    isSwizzled: isSwizzled));
                entries[index] = ToEntry(gameId, entry, isSwizzled, null, canonical);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
            {
                entries[index] = ToEntry(
                    gameId,
                    entry,
                    isSwizzled,
                    $"FX texture {entry.Index} could not be normalized: {exception.Message}",
                    []);
            }
        }

        var capabilities = gameId == GameId.UYA
            ? new FxTextureCapabilities(true, true, true, null)
            : new FxTextureCapabilities(true, false, false, DlAuthoringDisabled);
        return new(gameId, capabilities, entries);
    }

    public static FxTextureComposition Compose(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> storedAssetBytes,
        IReadOnlyList<FxTextureReplacement> replacements,
        IReadOnlyList<FxIndexedTexture> additions,
        FxTextureCompositionOptions? options = null,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaFxTextureComposer.Compose(
                headerBytes,
                storedAssetBytes,
                replacements,
                additions,
                options,
                cancellationToken),
            _ => throw new NotSupportedException($"FX texture composition is not supported for {gameId}."),
        };

    public static string GetLabel(GameId gameId, int index)
    {
        var special = index switch
        {
            -8 => "FX_BACK_ALPHA_CLUT",
            -7 => "FX_RAW_FRONT_BUFFER",
            -6 => "FX_RAW_BACK_BUFFER",
            -5 => "FX_RAW_Z_BUFFER",
            -4 => "FX_BACK_BUFFER_RECOPY64",
            -3 => "FX_BACK_BUFFER_COPY64",
            -2 => "FX_BACK_BUFFER_RECOPY",
            -1 => "FX_BACK_BUFFER_COPY",
            _ => null,
        };
        if (special is not null) return special;

        var known = gameId switch
        {
            GameId.UYA => UyaFxTextureLabels.GetKnownName(index),
            GameId.DL => DlFxTextureLabels.GetKnownName(index),
            _ => throw new NotSupportedException($"FX texture labels are not supported for {gameId}."),
        };
        return known ?? $"FX_TEXTURE_{index}";
    }

    private static FxTextureCatalogEntry ToEntry(
        GameId gameId,
        FxTextureSourceEntry source,
        bool isSwizzled,
        string? diagnostic,
        byte[] canonicalTextureBytes) => new(
            source.Index,
            GetLabel(gameId, source.Index),
            source.Width,
            source.Height,
            "Indexed8",
            "Rgba32",
            source.PaletteOffset,
            FxTextureInventoryReader.PaletteLength,
            source.PixelOffset,
            source.PixelLength,
            isSwizzled,
            diagnostic is null,
            diagnostic,
            canonicalTextureBytes);
}
