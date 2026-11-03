using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Builders;

public static class UyaSkyboxConverter
{
    public static byte[] ConvertFromDl(byte[] bytes, int? sourceLevel, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        token.ThrowIfCancellationRequested();
        using var input = new MemoryStream(bytes, writable: false);
        var sky = SkyboxReader.Read(input, GameId.DL);
        var result = bytes.ToArray();
        foreach (var texture in sky.Textures)
        {
            token.ThrowIfCancellationRequested();
            var pif = PifWriter.Write(PifWriter.CreateIndexed8(texture.Width, texture.Height,
                texture.PaletteData, texture.PixelData, isSwizzled: true));
            var converted = PifReader.Read(UyaLevelAssetModelConverter.ConvertDlTexture(pif));
            converted.PixelData.CopyTo(result, checked((int)(sky.Header.TextureDataOffset + texture.TextureOffset)));
        }
        foreach (var (index, rotation) in SkyboxGameProfile.ForGame(GameId.DL).ShellRotationOverridesFor(sourceLevel, sky.Shells.Count))
        {
            var offset = checked((int)sky.Shells[index].Offset);
            if (rotation.RotationX is { } x) BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset + 4), x);
            if (rotation.RotationY is { } y) BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset + 6), y);
            if (rotation.RotationZ is { } z) BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset + 8), z);
        }
        using var verified = new MemoryStream(result, writable: false);
        _ = SkyboxReader.Read(verified, GameId.UYA);
        return result;
    }
}
