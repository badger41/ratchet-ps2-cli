using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Palettes;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelAssetModelConverter
{
    public static byte[] RebaseTieClass(byte[] bytes, int classId)
    {
        _ = RatchetPs2.Core.Ties.TieClassReader.Read(bytes);
        if (classId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(classId));
        var result = bytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x44), (ushort)classId);
        return result;
    }

    public static byte[] ConvertLinearTexture(byte[] bytes, TextureRole role)
    {
        var source = PifReader.Read(bytes);
        if (source.Encoding != PifTextureEncoding.Indexed8 || source.IsSwizzled)
            throw new InvalidDataException("Source to UYA conversion requires linear indexed8 textures.");
        var limit = role switch
        {
            TextureRole.Material => 2,
            TextureRole.Billboard => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        return PifWriter.Write(new(source.Header, source.Encoding, source.PaletteData, source.PixelData,
            source.MipPixelData.Take(limit).ToArray()));
    }

    public static byte[] ConvertDlTexture(byte[] bytes, TextureRole role = TextureRole.Material) =>
        ConvertDlTexture(PifReader.Read(bytes), previousLayout: false, MipLimit(role));

    public static byte[]? RepairPreviousDlTexture(byte[] sourceBytes, byte[] convertedBytes, TextureRole role = TextureRole.Material)
    {
        var limit = MipLimit(role);
        var source = PifReader.Read(sourceBytes);
        var previous = source.IsSwizzled
            ? ConvertDlTexture(source, previousLayout: false, source.MipPixelData.Count)
            : sourceBytes;
        if (!previous.AsSpan().SequenceEqual(convertedBytes)
            && !ConvertDlTexture(source, previousLayout: true, source.MipPixelData.Count).AsSpan().SequenceEqual(convertedBytes))
            return null;
        var corrected = ConvertDlTexture(source, previousLayout: false, limit);
        return corrected.AsSpan().SequenceEqual(convertedBytes) ? null : corrected;
    }

    private static int MipLimit(TextureRole role) => role switch
    {
        TextureRole.Material => 2,
        TextureRole.Billboard => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static byte[] ConvertDlTexture(PifTextureData source, bool previousLayout, int mipLimit)
    {
        if (source.Encoding != PifTextureEncoding.Indexed8)
            throw new InvalidDataException("UYA static models require indexed8 textures.");
        if (!source.IsSwizzled)
            return PifWriter.Write(new(source.Header, source.Encoding, source.PaletteData, source.PixelData,
                source.MipPixelData.Take(mipLimit).ToArray()));
        var width = source.Header.USize;
        var height = source.Header.VSize;
        var pixels = Unswizzle(source.PixelData, width, height)
            ?? throw new InvalidDataException("DL texture swizzle does not cover its image bounds.");
        var mips = new List<byte[]>();
        for (var index = 0; index < Math.Min(source.MipPixelData.Count, mipLimit); index++)
        {
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            var mip = source.MipPixelData[index];
            // The quarter-size mip comes from the GS palette bank and is already linear.
            var converted = index > 0 && !previousLayout ? mip.ToArray() : Unswizzle(mip, width, height);
            // shortcut: omit smaller mips when the SDK swizzle map is not bijective; extend after small-mip layout qualification.
            if (converted is null) break;
            mips.Add(converted);
        }
        return PifWriter.Write(new(source.Header with
        {
            TexFormat = source.Header.TexFormat & ~PifHeader.SwizzledFlag,
        }, source.Encoding, source.PaletteData, pixels, mips));
    }

    private static byte[]? Unswizzle(byte[] source, int width, int height)
    {
        if (width < 4 || height < 4) return source.ToArray();
        var output = new byte[source.Length];
        var written = new bool[source.Length];
        for (var index = 0; index < checked(width * height); index++)
        {
            var target = TextureConverter.RemapPixelIndexFromRac4(index, width);
            if ((uint)target >= (uint)output.Length || written[target]) return null;
            written[target] = true;
            output[target] = source[index];
        }
        return output;
    }

    public static byte[] ConvertDlTie(byte[] bytes, int targetClassId)
    {
        var source = TieClassReader.Read(bytes, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.DL)));
        var result = bytes.ToArray();
        // Remove DL's normal header in place so all relative remap offsets keep their original base.
        if (source.Header.VertexNormalsOffset != 0 && source.Header.VertexNormalsCount > 0)
        {
            var offset = checked((int)source.Header.VertexNormalsOffset);
            var length = checked(source.Header.VertexNormalsCount * 8);
            bytes.AsSpan(offset + 16, length).CopyTo(result.AsSpan(offset, length));
            result.AsSpan(offset + length, 16).Clear();
        }
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x44), checked((ushort)targetClassId));
        _ = TieClassReader.Read(result, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        return result;
    }

    public static byte[]? RepairPreviousDlTie(byte[] sourceBytes, byte[] convertedBytes, int targetClassId)
    {
        var source = TieClassReader.Read(sourceBytes, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.DL)));
        if (source.Header.VertexNormalsOffset == 0 || source.Header.VertexNormalsCount <= 0) return null;
        var previous = sourceBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(previous.AsSpan(0x34), checked(source.Header.VertexNormalsOffset + 16));
        BinaryPrimitives.WriteUInt16LittleEndian(previous.AsSpan(0x44), checked((ushort)targetClassId));
        return previous.AsSpan().SequenceEqual(convertedBytes) ? ConvertDlTie(sourceBytes, targetClassId) : null;
    }

    public static byte[] ConvertDlShrub(byte[] bytes, int targetClassId)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        _ = ShrubClassReader.Read(stream);
        var result = bytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x24), checked((ushort)targetClassId));
        return result;
    }
}
