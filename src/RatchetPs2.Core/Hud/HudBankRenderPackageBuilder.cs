using System.Text.Json;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Hud;

public static class HudBankRenderPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static IReadOnlyList<PackedFile> BuildFiles(
        byte[] headerBytes,
        IReadOnlyList<byte[]> bankBytes)
    {
        ArgumentNullException.ThrowIfNull(headerBytes);
        ArgumentNullException.ThrowIfNull(bankBytes);

        var banks = Enumerable.Range(0, HudBankReader.BankCount)
            .Select(index => index < bankBytes.Count ? Decompress(bankBytes[index]) : [])
            .ToArray();
        var hud = HudBankReader.Read(headerBytes, banks);
        var files = new List<PackedFile>();
        var normalizedTextures = new List<object>(hud.Frames.Count);

        foreach (var frame in hud.Frames)
        {
            var hasTexture = HudBankReader.TryGetTexture(hud, frame.TextureIndex, out var texture);
            var hasPalette = HudBankReader.TryGetPalette(hud, frame.PaletteIndex, out var palette);
            string? pngPath = null;
            var status = "skipped";
            string? note = null;

            if (hasTexture && hasPalette)
            {
                try
                {
                    pngPath = $"bank_{texture.BankIndex}/tex.{frame.Index:0000}.png";
                    var pif = PifWriter.CreateIndexed8(
                        texture.Width,
                        texture.Height,
                        palette.PaletteBytes,
                        texture.PixelBytes,
                        isSwizzled: false);
                    files.Add(new PackedFile(
                        $"hud/{pngPath}",
                        TextureConverter.ConvertToPng(pif, TexturePixelFormat.Rgba32),
                        "image/png"));
                    status = "written";
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
                {
                    pngPath = null;
                    status = "error";
                    note = ex.Message;
                }
            }
            else
            {
                note = hasTexture
                    ? $"palette index {frame.PaletteIndex} is missing or out of range"
                    : $"texture index {frame.TextureIndex} is missing or out of range";
            }

            normalizedTextures.Add(new
            {
                FrameIndex = frame.Index,
                frame.PaletteIndex,
                frame.TextureIndex,
                TextureBank = hasTexture ? texture.BankIndex : (int?)null,
                PaletteBank = hasPalette ? palette.BankIndex : (int?)null,
                Status = status,
                Note = note,
                PngPath = pngPath,
                Texture = hasTexture ? new { texture.Index, texture.Width, texture.Height } : null
            });
        }

        files.Add(new PackedFile(
            "hud/manifest.json",
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                Banks = Enumerable.Range(0, HudBankReader.BankCount).Select(index => new
                {
                    BankIndex = index,
                    Length = banks[index].Length,
                    DeclaredDecompressedSize = index < hud.Header.BankSizes.Count
                        ? hud.Header.BankSizes[index]
                        : 0
                }),
                NormalizedFrameTextures = normalizedTextures
            }, JsonOptions),
            "application/json"));
        return files;
    }

    private static byte[] Decompress(byte[] bytes) =>
        BinaryMagic.IsWad(bytes) ? WadCompression.Decompress(bytes) : bytes;
}
