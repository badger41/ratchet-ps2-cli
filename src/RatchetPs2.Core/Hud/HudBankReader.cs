using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Hud;

public static class HudBankReader
{
    public const int HeaderFixedLength = 0xb4;
    public const int BankCount = 5;
    public const int PaletteLength = 0x400;
    public const int MaximumIconMappings = 1024;
    public const int CpuBankAlignment = 0x10;
    public const int DmaBankAlignment = 0x40;
    public const uint IconMappingTerminator = 0x0000ffff;
    public const uint SerializedPayloadOffsetMask = 0x7fffffff;

    public static HudBankSet Read(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> bankBytes)
    {
        ArgumentNullException.ThrowIfNull(bankBytes);

        if (headerBytes.Length < HeaderFixedLength)
        {
            throw new InvalidDataException("HUD header is too small.");
        }

        var banks = Enumerable.Range(0, BankCount)
            .Select(index => index < bankBytes.Count ? bankBytes[index] : [])
            .ToArray();
        var header = ReadHeader(headerBytes);
        ValidateCumulativeCounts(header.PaletteCumulativeCounts, "palette");
        ValidateCumulativeCounts(header.TextureCumulativeCounts, "texture");
        var (icons, iconTerminatorOffset) = ReadIcons(headerBytes, header);
        ValidateTableRange(headerBytes, header.FrameListOffset, header.FrameCount, 4, "HUD frame table");
        ValidateTableRange(
            headerBytes, header.PaletteListOffset, GetFinalCount(header.PaletteCumulativeCounts), 8,
            "HUD palette table");
        ValidateTableRange(
            headerBytes, header.TextureListOffset, GetFinalCount(header.TextureCumulativeCounts), 8,
            "HUD texture table");
        var frames = ReadFrames(headerBytes, header);
        ValidateIconFrameRanges(icons, frames.Count);
        var palettes = ReadPalettes(headerBytes, banks, header);
        var textures = ReadTextures(headerBytes, banks, header);

        return new HudBankSet(header, icons, frames, palettes, textures, iconTerminatorOffset);
    }

    public static bool TryGetPalette(HudBankSet hud, int paletteId, out HudPaletteEntry palette)
    {
        palette = hud.Palettes.FirstOrDefault(entry => entry.Index == paletteId)!;
        return palette is not null && palette.IsLengthValid;
    }

    public static bool TryGetTexture(HudBankSet hud, int textureId, out HudTextureEntry texture)
    {
        texture = hud.Textures.FirstOrDefault(entry => entry.Index == textureId)!;
        return texture is not null && texture.IsLengthValid;
    }

    private static HudHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        var paletteCounts = ReadInt32Array(data, 0x14, BankCount);
        var textureCounts = ReadInt32Array(data, 0x34, BankCount);
        return new HudHeader(
            ReadUInt16LittleEndian(data, 0x00),
            ReadUInt16LittleEndian(data, 0x02),
            ReadInt32LittleEndian(data, 0x04),
            ReadInt32LittleEndian(data, 0x08),
            ReadInt32LittleEndian(data, 0x0c),
            ReadInt32LittleEndian(data, 0x10),
            paletteCounts,
            ReadInt32Array(data, 0x28, 3),
            textureCounts,
            ReadInt32Array(data, 0x48, 3),
            ReadInt32Array(data, 0x54, BankCount),
            data.Slice(0x68, HeaderFixedLength - 0x68).ToArray());
    }

    private static (IReadOnlyList<HudIconEntry> Icons, int TerminatorOffset) ReadIcons(
        ReadOnlySpan<byte> headerBytes,
        HudHeader header)
    {
        if (header.IconCount is 0 or > MaximumIconMappings + 1)
        {
            throw new InvalidDataException(
                $"HUD icon record count must include one terminator and at most {MaximumIconMappings} mappings.");
        }

        var mappingCount = header.IconCount - 1;
        var icons = new List<HudIconEntry>(mappingCount);
        var spriteIds = new HashSet<ushort>();
        for (var i = 0; i < mappingCount; i++)
        {
            var offset = checked(header.IconListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "HUD icon entry");
            var icon = new HudIconEntry(
                i,
                ReadUInt16LittleEndian(headerBytes, offset),
                ReadUInt16LittleEndian(headerBytes, offset + 2),
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                ReadUInt16LittleEndian(headerBytes, offset + 6));
            if (!spriteIds.Add(icon.IconId))
            {
                throw new InvalidDataException($"HUD sprite ID 0x{icon.IconId:X4} is duplicated.");
            }

            icons.Add(icon);
        }

        var terminatorOffset = checked(header.IconListOffset + (mappingCount * 8));
        ValidateHeaderRange(headerBytes, terminatorOffset, 4, "HUD icon terminator");
        var terminator = ReadUInt32LittleEndian(headerBytes, terminatorOffset);
        if (terminator != IconMappingTerminator)
        {
            throw new InvalidDataException(
                $"HUD icon table has terminator 0x{terminator:X8}; expected 0x{IconMappingTerminator:X8}.");
        }

        return (icons, terminatorOffset);
    }

    private static IReadOnlyList<HudFrameEntry> ReadFrames(ReadOnlySpan<byte> headerBytes, HudHeader header)
    {
        var frames = new List<HudFrameEntry>(header.FrameCount);
        for (var i = 0; i < header.FrameCount; i++)
        {
            var offset = checked(header.FrameListOffset + (i * 4));
            ValidateHeaderRange(headerBytes, offset, 4, "HUD frame entry");
            frames.Add(new HudFrameEntry(
                i,
                ReadInt16LittleEndian(headerBytes, offset),
                ReadInt16LittleEndian(headerBytes, offset + 2)));
        }

        return frames;
    }

    private static IReadOnlyList<HudPaletteEntry> ReadPalettes(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> banks,
        HudHeader header)
    {
        var paletteCount = GetFinalCount(header.PaletteCumulativeCounts);
        var palettes = new List<HudPaletteEntry>(paletteCount);
        for (var i = 0; i < paletteCount; i++)
        {
            var offset = checked(header.PaletteListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "HUD palette entry");
            var bankIndex = GetBankIndex(header.PaletteCumulativeCounts, i);
            var encodedOffset = ReadUInt32LittleEndian(headerBytes, offset);
            var paletteOffset = DecodePayloadOffset(encodedOffset);
            var bankBytes = banks[bankIndex].AsSpan();
            var isLengthValid = IsPayloadRangeValid(bankBytes, paletteOffset, PaletteLength);
            palettes.Add(new HudPaletteEntry(
                i,
                bankIndex,
                encodedOffset,
                paletteOffset,
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                ReadUInt16LittleEndian(headerBytes, offset + 6),
                isLengthValid,
                isLengthValid
                    ? bankBytes.Slice(paletteOffset, PaletteLength).ToArray()
                    : []));
        }

        return palettes;
    }

    private static IReadOnlyList<HudTextureEntry> ReadTextures(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> banks,
        HudHeader header)
    {
        var textureCount = GetFinalCount(header.TextureCumulativeCounts);
        var textures = new List<HudTextureEntry>(textureCount);
        for (var i = 0; i < textureCount; i++)
        {
            var offset = checked(header.TextureListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "HUD texture entry");
            var bankIndex = GetBankIndex(header.TextureCumulativeCounts, i);
            var encodedOffset = ReadUInt32LittleEndian(headerBytes, offset);
            var textureOffset = DecodePayloadOffset(encodedOffset);
            var uLog = headerBytes[offset + 6];
            var vLog = headerBytes[offset + 7];
            var width = DimensionFromLog(uLog);
            var height = DimensionFromLog(vLog);
            var pixelLength = checked(width * height);
            var bankBytes = banks[bankIndex].AsSpan();
            var isLengthValid = IsPayloadRangeValid(bankBytes, textureOffset, pixelLength);

            textures.Add(new HudTextureEntry(
                i,
                bankIndex,
                encodedOffset,
                textureOffset,
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                uLog,
                vLog,
                width,
                height,
                pixelLength,
                isLengthValid,
                isLengthValid
                    ? bankBytes.Slice(textureOffset, pixelLength).ToArray()
                    : []));
        }

        return textures;
    }

    private static IReadOnlyList<int> ReadInt32Array(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadInt32LittleEndian(data, offset + (i * 4));
        }

        return values;
    }

    private static int GetFinalCount(IReadOnlyList<int> cumulativeCounts)
    {
        var count = cumulativeCounts.Count == 0 ? 0 : cumulativeCounts.Max();
        if (count < 0)
        {
            throw new InvalidDataException("HUD cumulative count is negative.");
        }

        return count;
    }

    private static int GetBankIndex(IReadOnlyList<int> cumulativeCounts, int itemIndex)
    {
        for (var bank = 0; bank < cumulativeCounts.Count; bank++)
        {
            if (itemIndex < cumulativeCounts[bank])
            {
                return bank;
            }
        }

        throw new InvalidDataException($"HUD item index {itemIndex} is outside cumulative bank counts.");
    }

    private static int DimensionFromLog(byte log)
    {
        if (log > 30)
        {
            throw new InvalidDataException($"HUD texture dimension log {log} is too large.");
        }

        return 1 << log;
    }

    private static int DecodePayloadOffset(uint encodedOffset)
    {
        return checked((int)(encodedOffset & SerializedPayloadOffsetMask));
    }

    private static void ValidateCumulativeCounts(IReadOnlyList<int> counts, string label)
    {
        var previous = 0;
        for (var bank = 0; bank < counts.Count; bank++)
        {
            var count = counts[bank];
            if (count < previous)
            {
                throw new InvalidDataException(
                    $"HUD {label} cumulative count decreases at bank {bank}: {count} < {previous}.");
            }

            previous = count;
        }
    }

    private static void ValidateIconFrameRanges(IReadOnlyList<HudIconEntry> icons, int frameCount)
    {
        foreach (var icon in icons)
        {
            if ((long)icon.FirstFrameIndex + icon.FrameCount > frameCount)
            {
                throw new InvalidDataException(
                    $"HUD sprite ID 0x{icon.IconId:X4} references frames outside the frame table.");
            }
        }
    }

    private static bool IsPayloadRangeValid(ReadOnlySpan<byte> data, int offset, int length)
    {
        return offset >= 0 && length >= 0 && (long)offset + length <= data.Length;
    }

    private static void ValidateHeaderRange(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (!IsPayloadRangeValid(data, offset, length))
        {
            throw new InvalidDataException($"{label} points outside the HUD header.");
        }
    }

    private static void ValidateTableRange(
        ReadOnlySpan<byte> data,
        int offset,
        int count,
        int recordLength,
        string label)
    {
        var length = (long)count * recordLength;
        if (count < 0 || offset < 0 || length > int.MaxValue
            || (long)offset + length > data.Length)
            throw new InvalidDataException($"{label} points outside the HUD header.");
    }

}

public sealed record HudBankSet(
    HudHeader Header,
    IReadOnlyList<HudIconEntry> Icons,
    IReadOnlyList<HudFrameEntry> Frames,
    IReadOnlyList<HudPaletteEntry> Palettes,
    IReadOnlyList<HudTextureEntry> Textures,
    int IconTerminatorOffset);

public sealed record HudHeader(
    ushort IconCount,
    ushort FrameCount,
    int IconListOffset,
    int FrameListOffset,
    int PaletteListOffset,
    int TextureListOffset,
    IReadOnlyList<int> PaletteCumulativeCounts,
    IReadOnlyList<int> UnknownCounts28,
    IReadOnlyList<int> TextureCumulativeCounts,
    IReadOnlyList<int> UnknownCounts48,
    IReadOnlyList<int> BankSizes,
    byte[] RuntimePointerArea)
{
    public int IconMappingCount => IconCount - 1;
}

public sealed record HudIconEntry(
    int Index,
    ushort IconId,
    ushort FrameCount,
    ushort FirstFrameIndex,
    ushort Padding);

public sealed record HudFrameEntry(
    int Index,
    short PaletteIndex,
    short TextureIndex);

public sealed record HudPaletteEntry(
    int Index,
    int BankIndex,
    uint EncodedOffset,
    int Offset,
    ushort GsRam,
    ushort Padding,
    bool IsLengthValid,
    byte[] PaletteBytes);

public sealed record HudTextureEntry(
    int Index,
    int BankIndex,
    uint EncodedOffset,
    int Offset,
    ushort GsRam,
    byte ULog,
    byte VLog,
    int Width,
    int Height,
    int PixelLength,
    bool IsLengthValid,
    byte[] PixelBytes);
