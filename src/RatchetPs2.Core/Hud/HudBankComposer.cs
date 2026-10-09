using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.Hud;

public sealed record HudIndexedTexture(
    int Width,
    int Height,
    ReadOnlyMemory<byte> PaletteBytes,
    ReadOnlyMemory<byte> PixelBytes);

public sealed record HudTextureReplacement(
    int FrameIndex,
    HudIndexedTexture Texture);

public sealed record HudIconAddition(
    ushort SpriteId,
    int BankIndex,
    HudIndexedTexture Texture);

public sealed record HudBankCompositionOptions(
    int MaximumHeaderBytes = 4 * 1024 * 1024,
    int MaximumDecompressedBankBytes = 64 * 1024 * 1024,
    int MaximumCompressedBankBytes = 64 * 1024 * 1024,
    IReadOnlyList<int>? BankCapacities = null);

public sealed record HudBankComposition(
    byte[] HeaderBytes,
    IReadOnlyList<byte[]> BankBytes,
    bool IsBasePassThrough);

public static class HudBankComposer
{
    private sealed record PlannedReplacement(
        HudTextureReplacement Edit,
        int BankIndex,
        int PaletteIndex,
        int TextureIndex,
        int PaletteOffset,
        int TextureOffset);

    private sealed record PlannedAddition(
        HudIconAddition Edit,
        int PaletteIndex,
        int TextureIndex,
        int FrameIndex,
        int PaletteOffset,
        int TextureOffset);

    public static HudBankComposition Compose(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> bankBytes,
        IReadOnlyList<HudTextureReplacement> replacements,
        IReadOnlyList<HudIconAddition> additions,
        HudBankCompositionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bankBytes);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(additions);
        options ??= new();
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (bankBytes.Count != HudBankReader.BankCount)
        {
            throw new InvalidDataException(
                $"HUD composition requires exactly {HudBankReader.BankCount} physical banks; received {bankBytes.Count}.");
        }
        if (headerBytes.Length > options.MaximumHeaderBytes)
        {
            throw new InvalidDataException(
                $"HUD header size 0x{headerBytes.Length:X} exceeds the configured 0x{options.MaximumHeaderBytes:X}-byte limit.");
        }

        var sourceHeader = headerBytes.ToArray();
        var sourceBankBytes = bankBytes.Select(bytes => bytes ?? throw new ArgumentException(
            "HUD bank bytes cannot contain null entries.", nameof(bankBytes))).ToArray();
        var sourceWasCompressed = sourceBankBytes.Select(bytes => BinaryMagic.IsWad(bytes)).ToArray();
        var sourceBanks = ReadBanks(sourceBankBytes, sourceWasCompressed, options, cancellationToken);
        var source = HudBankReader.Read(sourceHeader, sourceBanks);
        ValidateSource(source, sourceBanks, options);

        if (replacements.Count == 0 && additions.Count == 0)
        {
            return new HudBankComposition(
                sourceHeader,
                sourceBankBytes.Select(bytes => bytes.ToArray()).ToArray(),
                true);
        }

        var plannedLengths = sourceBanks.Select(bytes => (long)bytes.Length).ToArray();
        var changedBanks = new bool[HudBankReader.BankCount];
        var plannedReplacements = PlanReplacements(
            source, replacements, plannedLengths, changedBanks, options, cancellationToken);
        var plannedAdditions = PlanAdditions(
            source, additions, replacements.Count, plannedLengths, changedBanks, options, cancellationToken);
        var headerLength = PlanHeaderLength(
            sourceHeader.Length, source, replacements.Count, additions.Count, options);

        cancellationToken.ThrowIfCancellationRequested();
        var outputBanks = BuildBanks(
            sourceBanks, plannedLengths, changedBanks, plannedReplacements, plannedAdditions, cancellationToken);
        var outputHeader = BuildHeader(
            sourceHeader, source, outputBanks, plannedReplacements, plannedAdditions, headerLength);
        var serializedBanks = SerializeBanks(
            sourceBankBytes, sourceWasCompressed, outputBanks, changedBanks, options, cancellationToken);

        Verify(
            source,
            sourceBanks,
            outputHeader,
            serializedBanks,
            sourceWasCompressed,
            plannedReplacements,
            plannedAdditions,
            options,
            cancellationToken);
        return new HudBankComposition(outputHeader, serializedBanks, false);
    }

    private static byte[][] ReadBanks(
        IReadOnlyList<byte[]> bankBytes,
        IReadOnlyList<bool> compressed,
        HudBankCompositionOptions options,
        CancellationToken cancellationToken)
    {
        var banks = new byte[HudBankReader.BankCount][];
        var wadOptions = new WadDecompressionOptions(options.MaximumDecompressedBankBytes);
        for (var bank = 0; bank < banks.Length; bank++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bankBytes[bank].Length > options.MaximumCompressedBankBytes)
            {
                throw new InvalidDataException(
                    $"HUD bank {bank} stored size 0x{bankBytes[bank].Length:X} exceeds the configured " +
                    $"0x{options.MaximumCompressedBankBytes:X}-byte limit.");
            }

            try
            {
                banks[bank] = compressed[bank]
                    ? WadCompression.Decompress(bankBytes[bank], wadOptions, cancellationToken)
                    : bankBytes[bank].ToArray();
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException)
            {
                throw new InvalidDataException($"HUD bank {bank} could not be decompressed: {exception.Message}", exception);
            }

            if (banks[bank].Length > BankCapacity(options, bank))
            {
                throw new InvalidDataException(
                    $"HUD bank {bank} decompressed size 0x{banks[bank].Length:X} exceeds its " +
                    $"0x{BankCapacity(options, bank):X}-byte capacity.");
            }
        }
        return banks;
    }

    private static void ValidateSource(
        HudBankSet source,
        IReadOnlyList<byte[]> banks,
        HudBankCompositionOptions options)
    {
        if (source.Palettes.Count > short.MaxValue + 1 || source.Textures.Count > short.MaxValue + 1)
        {
            throw new InvalidDataException("HUD palette or texture count exceeds signed frame-table addressing.");
        }

        for (var bank = 0; bank < HudBankReader.BankCount; bank++)
        {
            var declared = source.Header.BankSizes[bank];
            if (declared < 0 || declared != banks[bank].Length)
            {
                throw new InvalidDataException(
                    $"HUD bank {bank} declares 0x{declared:X} decompressed bytes but contains 0x{banks[bank].Length:X}.");
            }
            if (declared > BankCapacity(options, bank))
            {
                throw new InvalidDataException(
                    $"HUD bank {bank} declared size exceeds its configured capacity.");
            }
        }

        foreach (var frame in source.Frames)
        {
            if (frame.PaletteIndex < 0 || frame.PaletteIndex >= source.Palettes.Count
                || frame.TextureIndex < 0 || frame.TextureIndex >= source.Textures.Count)
            {
                throw new InvalidDataException(
                    $"HUD frame {frame.Index} references palette {frame.PaletteIndex} and texture {frame.TextureIndex} outside the tables.");
            }
        }
        foreach (var palette in source.Palettes)
        {
            if (!palette.IsLengthValid || (palette.EncodedOffset & 0x80000000) == 0
                || palette.Offset % 0x100 != 0)
            {
                throw new InvalidDataException(
                    $"HUD palette {palette.Index} in bank {palette.BankIndex} has an invalid serialized range or alignment.");
            }
        }
        foreach (var texture in source.Textures)
        {
            if (!texture.IsLengthValid || (texture.EncodedOffset & 0x80000000) == 0
                || texture.Offset % 0x40 != 0)
            {
                throw new InvalidDataException(
                    $"HUD texture {texture.Index} in bank {texture.BankIndex} has an invalid serialized range or alignment.");
            }
        }
    }

    private static IReadOnlyList<PlannedReplacement> PlanReplacements(
        HudBankSet source,
        IReadOnlyList<HudTextureReplacement> replacements,
        long[] plannedLengths,
        bool[] changedBanks,
        HudBankCompositionOptions options,
        CancellationToken cancellationToken)
    {
        if (source.Palettes.Count + replacements.Count > short.MaxValue + 1
            || source.Textures.Count + replacements.Count > short.MaxValue + 1)
            throw new InvalidDataException("HUD replacements exceed signed frame-table addressing.");

        var frameIndexes = new HashSet<int>();
        var bank = Math.Max(
            LastPopulatedBank(source.Header.PaletteCumulativeCounts),
            LastPopulatedBank(source.Header.TextureCumulativeCounts));
        var plans = new List<PlannedReplacement>(replacements.Count);
        for (var editIndex = 0; editIndex < replacements.Count; editIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edit = replacements[editIndex];
            ArgumentNullException.ThrowIfNull(edit.Texture);
            ValidateTexture(edit.Texture, $"HUD replacement {editIndex}");
            if ((uint)edit.FrameIndex >= (uint)source.Frames.Count)
                throw new InvalidDataException($"HUD replacement {editIndex} frame index {edit.FrameIndex} is out of range.");
            if (!frameIndexes.Add(edit.FrameIndex))
                throw new InvalidDataException($"HUD replacement {editIndex} targets frame {edit.FrameIndex} more than once.");

            var paletteOffset = PlanPayload(
                plannedLengths, bank, 0x100, HudBankReader.PaletteLength,
                $"HUD replacement {editIndex} palette", options);
            var textureOffset = PlanPayload(
                plannedLengths, bank, 0x40, edit.Texture.PixelBytes.Length,
                $"HUD replacement {editIndex} texture", options);
            changedBanks[bank] = true;
            plans.Add(new(
                edit,
                bank,
                source.Palettes.Count + editIndex,
                source.Textures.Count + editIndex,
                paletteOffset,
                textureOffset));
        }
        return plans;
    }

    private static IReadOnlyList<PlannedAddition> PlanAdditions(
        HudBankSet source,
        IReadOnlyList<HudIconAddition> additions,
        int replacementCount,
        long[] plannedLengths,
        bool[] changedBanks,
        HudBankCompositionOptions options,
        CancellationToken cancellationToken)
    {
        if (source.Icons.Count + additions.Count > HudBankReader.MaximumIconMappings)
            throw new InvalidDataException($"HUD additions exceed the {HudBankReader.MaximumIconMappings}-mapping runtime limit.");
        if (source.Frames.Count + additions.Count > ushort.MaxValue)
            throw new InvalidDataException("HUD additions exceed the 16-bit frame-count limit.");
        if (source.Palettes.Count + replacementCount + additions.Count > short.MaxValue + 1
            || source.Textures.Count + replacementCount + additions.Count > short.MaxValue + 1)
            throw new InvalidDataException("HUD additions exceed signed frame-table addressing.");

        var occupiedIds = source.Icons.Select(icon => icon.IconId).ToHashSet();
        var minimumBank = Math.Max(
            LastPopulatedBank(source.Header.PaletteCumulativeCounts),
            LastPopulatedBank(source.Header.TextureCumulativeCounts));
        var previousBank = minimumBank;
        var plans = new List<PlannedAddition>(additions.Count);
        for (var additionIndex = 0; additionIndex < additions.Count; additionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edit = additions[additionIndex];
            ArgumentNullException.ThrowIfNull(edit.Texture);
            ValidateTexture(edit.Texture, $"HUD addition {additionIndex}");
            if ((uint)edit.BankIndex >= HudBankReader.BankCount)
                throw new InvalidDataException($"HUD addition {additionIndex} bank {edit.BankIndex} is outside 0..4.");
            if (edit.BankIndex < previousBank)
            {
                throw new InvalidDataException(
                    $"HUD addition {additionIndex} bank {edit.BankIndex} would renumber records already assigned through bank {previousBank}.");
            }
            if (edit.SpriteId == HudBankReader.IconMappingTerminator || !occupiedIds.Add(edit.SpriteId))
                throw new InvalidDataException($"HUD addition {additionIndex} sprite ID 0x{edit.SpriteId:X4} is reserved or duplicated.");

            var paletteOffset = PlanPayload(
                plannedLengths, edit.BankIndex, 0x100, HudBankReader.PaletteLength,
                $"HUD addition {additionIndex} palette", options);
            var textureOffset = PlanPayload(
                plannedLengths, edit.BankIndex, 0x40, edit.Texture.PixelBytes.Length,
                $"HUD addition {additionIndex} texture", options);
            changedBanks[edit.BankIndex] = true;
            plans.Add(new(
                edit,
                source.Palettes.Count + replacementCount + additionIndex,
                source.Textures.Count + replacementCount + additionIndex,
                source.Frames.Count + additionIndex,
                paletteOffset,
                textureOffset));
            previousBank = edit.BankIndex;
        }
        return plans;
    }

    private static int PlanHeaderLength(
        int sourceLength,
        HudBankSet source,
        int replacementCount,
        int additionCount,
        HudBankCompositionOptions options)
    {
        if (replacementCount == 0 && additionCount == 0) return sourceLength;
        var length = Align(sourceLength, 4);
        length = checked(length + ((source.Icons.Count + additionCount) * 8L) + 4);
        length = Align(length, 4);
        length = checked(length + ((source.Frames.Count + additionCount) * 4L));
        length = Align(length, 4);
        length = checked(length + ((source.Palettes.Count + replacementCount + additionCount) * 8L));
        length = Align(length, 4);
        length = checked(length + ((source.Textures.Count + replacementCount + additionCount) * 8L));
        if (length > options.MaximumHeaderBytes || length > int.MaxValue)
            throw new InvalidDataException($"Composed HUD header size 0x{length:X} exceeds its configured capacity.");
        return (int)length;
    }

    private static byte[][] BuildBanks(
        IReadOnlyList<byte[]> sourceBanks,
        IReadOnlyList<long> plannedLengths,
        IReadOnlyList<bool> changedBanks,
        IReadOnlyList<PlannedReplacement> replacements,
        IReadOnlyList<PlannedAddition> additions,
        CancellationToken cancellationToken)
    {
        var output = new byte[HudBankReader.BankCount][];
        for (var bank = 0; bank < output.Length; bank++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!changedBanks[bank])
            {
                output[bank] = sourceBanks[bank];
                continue;
            }
            output[bank] = new byte[checked((int)plannedLengths[bank])];
            sourceBanks[bank].CopyTo(output[bank], 0);
        }
        foreach (var plan in replacements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.Edit.Texture.PaletteBytes.Span.CopyTo(output[plan.BankIndex].AsSpan(plan.PaletteOffset));
            plan.Edit.Texture.PixelBytes.Span.CopyTo(output[plan.BankIndex].AsSpan(plan.TextureOffset));
        }
        foreach (var plan in additions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.Edit.Texture.PaletteBytes.Span.CopyTo(output[plan.Edit.BankIndex].AsSpan(plan.PaletteOffset));
            plan.Edit.Texture.PixelBytes.Span.CopyTo(output[plan.Edit.BankIndex].AsSpan(plan.TextureOffset));
        }
        return output;
    }

    private static byte[] BuildHeader(
        byte[] sourceHeader,
        HudBankSet source,
        IReadOnlyList<byte[]> banks,
        IReadOnlyList<PlannedReplacement> replacements,
        IReadOnlyList<PlannedAddition> additions,
        int headerLength)
    {
        var output = new byte[headerLength];
        sourceHeader.CopyTo(output, 0);
        for (var bank = 0; bank < HudBankReader.BankCount; bank++)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x54 + bank * 4, 4), banks[bank].Length);
        var cursor = checked((int)Align(sourceHeader.Length, 4));
        var iconOffset = cursor;
        sourceHeader.AsSpan(source.Header.IconListOffset, source.Icons.Count * 8).CopyTo(output.AsSpan(cursor));
        cursor += source.Icons.Count * 8;
        foreach (var plan in additions)
        {
            var record = output.AsSpan(cursor, 8);
            BinaryPrimitives.WriteUInt16LittleEndian(record, plan.Edit.SpriteId);
            BinaryPrimitives.WriteUInt16LittleEndian(record[2..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(record[4..], checked((ushort)plan.FrameIndex));
            cursor += 8;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(cursor, 4), HudBankReader.IconMappingTerminator);
        cursor = checked((int)Align(cursor + 4, 4));

        var frameOffset = cursor;
        sourceHeader.AsSpan(source.Header.FrameListOffset, source.Frames.Count * 4).CopyTo(output.AsSpan(cursor));
        foreach (var plan in replacements)
        {
            var record = output.AsSpan(frameOffset + plan.Edit.FrameIndex * 4, 4);
            BinaryPrimitives.WriteInt16LittleEndian(record, checked((short)plan.PaletteIndex));
            BinaryPrimitives.WriteInt16LittleEndian(record[2..], checked((short)plan.TextureIndex));
        }
        cursor += source.Frames.Count * 4;
        foreach (var plan in additions)
        {
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(cursor, 2), checked((short)plan.PaletteIndex));
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(cursor + 2, 2), checked((short)plan.TextureIndex));
            cursor += 4;
        }
        cursor = checked((int)Align(cursor, 4));

        var paletteOffset = cursor;
        sourceHeader.AsSpan(source.Header.PaletteListOffset, source.Palettes.Count * 8).CopyTo(output.AsSpan(cursor));
        cursor += source.Palettes.Count * 8;
        foreach (var plan in replacements)
        {
            WritePaletteRecord(output.AsSpan(cursor, 8), plan.PaletteOffset, preserveMetadata: false);
            cursor += 8;
        }
        foreach (var plan in additions)
        {
            WritePaletteRecord(output.AsSpan(cursor, 8), plan.PaletteOffset, preserveMetadata: false);
            cursor += 8;
        }
        cursor = checked((int)Align(cursor, 4));

        var textureOffset = cursor;
        sourceHeader.AsSpan(source.Header.TextureListOffset, source.Textures.Count * 8).CopyTo(output.AsSpan(cursor));
        cursor += source.Textures.Count * 8;
        foreach (var plan in replacements)
        {
            WriteTextureRecord(
                output.AsSpan(cursor, 8),
                plan.TextureOffset,
                plan.Edit.Texture.Width,
                plan.Edit.Texture.Height,
                preserveGsRam: false);
            cursor += 8;
        }
        foreach (var plan in additions)
        {
            WriteTextureRecord(
                output.AsSpan(cursor, 8),
                plan.TextureOffset,
                plan.Edit.Texture.Width,
                plan.Edit.Texture.Height,
                preserveGsRam: false);
            cursor += 8;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(output, checked((ushort)(source.Icons.Count + additions.Count + 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), checked((ushort)(source.Frames.Count + additions.Count)));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), iconOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), frameOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x0c), paletteOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x10), textureOffset);
        WriteCumulativeCounts(output, 0x14, source.Header.PaletteCumulativeCounts, replacements, additions);
        WriteCumulativeCounts(output, 0x34, source.Header.TextureCumulativeCounts, replacements, additions);
        return output;
    }

    private static IReadOnlyList<byte[]> SerializeBanks(
        IReadOnlyList<byte[]> sourceBankBytes,
        IReadOnlyList<bool> sourceWasCompressed,
        IReadOnlyList<byte[]> outputBanks,
        IReadOnlyList<bool> changedBanks,
        HudBankCompositionOptions options,
        CancellationToken cancellationToken)
    {
        var result = new byte[HudBankReader.BankCount][];
        for (var bank = 0; bank < result.Length; bank++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!changedBanks[bank])
            {
                result[bank] = sourceBankBytes[bank].ToArray();
                continue;
            }

            if (!sourceWasCompressed[bank])
            {
                result[bank] = outputBanks[bank];
            }
            else
            {
                try
                {
                    result[bank] = WadCompression.CompressVerified(
                        outputBanks[bank],
                        new WadDecompressionOptions(options.MaximumDecompressedBankBytes),
                        cancellationToken).CompressedBytes;
                }
                catch (Exception exception) when (exception is InvalidDataException or OverflowException)
                {
                    throw new InvalidDataException($"HUD bank {bank} could not be recompressed: {exception.Message}", exception);
                }
            }
            if (result[bank].Length > options.MaximumCompressedBankBytes)
            {
                throw new InvalidDataException(
                    $"HUD bank {bank} stored size 0x{result[bank].Length:X} exceeds the configured " +
                    $"0x{options.MaximumCompressedBankBytes:X}-byte limit.");
            }
        }
        return result;
    }

    private static void Verify(
        HudBankSet source,
        IReadOnlyList<byte[]> sourceBanks,
        byte[] header,
        IReadOnlyList<byte[]> serializedBanks,
        IReadOnlyList<bool> compressed,
        IReadOnlyList<PlannedReplacement> replacements,
        IReadOnlyList<PlannedAddition> additions,
        HudBankCompositionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var banks = ReadBanks(serializedBanks, compressed, options, cancellationToken);
        var reread = HudBankReader.Read(header, banks);
        if (reread.Icons.Count != source.Icons.Count + additions.Count
            || reread.Frames.Count != source.Frames.Count + additions.Count
            || reread.Palettes.Count != source.Palettes.Count + replacements.Count + additions.Count
            || reread.Textures.Count != source.Textures.Count + replacements.Count + additions.Count)
            throw new InvalidDataException("Composed HUD table counts failed semantic verification.");

        for (var bank = 0; bank < HudBankReader.BankCount; bank++)
        {
            if (!banks[bank].AsSpan(0, sourceBanks[bank].Length).SequenceEqual(sourceBanks[bank]))
                throw new InvalidDataException($"Composed HUD bank {bank} did not preserve its original byte prefix.");
        }
        for (var index = 0; index < source.Icons.Count; index++)
            if (reread.Icons[index] != source.Icons[index])
                throw new InvalidDataException($"Composed HUD icon mapping {index} changed unexpectedly.");
        var replacementFrames = replacements.ToDictionary(value => value.Edit.FrameIndex);
        for (var index = 0; index < source.Frames.Count; index++)
        {
            if (replacementFrames.TryGetValue(index, out var replacement))
            {
                var frame = reread.Frames[index];
                if (frame.Index != source.Frames[index].Index
                    || frame.PaletteIndex != replacement.PaletteIndex
                    || frame.TextureIndex != replacement.TextureIndex)
                    throw new InvalidDataException($"Composed HUD replacement frame {index} has an invalid mapping.");
            }
            else if (reread.Frames[index] != source.Frames[index])
                throw new InvalidDataException($"Composed HUD frame {index} changed unexpectedly.");
        }

        foreach (var plan in replacements)
        {
            VerifyTexture(
                reread.Palettes[plan.PaletteIndex],
                reread.Textures[plan.TextureIndex],
                plan.Edit.Texture,
                $"HUD replacement frame {plan.Edit.FrameIndex}");
            if (reread.Palettes[plan.PaletteIndex].BankIndex != plan.BankIndex
                || reread.Textures[plan.TextureIndex].BankIndex != plan.BankIndex)
                throw new InvalidDataException($"HUD replacement frame {plan.Edit.FrameIndex} bank routing failed.");
        }
        for (var index = 0; index < additions.Count; index++)
        {
            var plan = additions[index];
            var icon = reread.Icons[source.Icons.Count + index];
            var frame = reread.Frames[plan.FrameIndex];
            if (icon.IconId != plan.Edit.SpriteId || icon.FrameCount != 1 || icon.FirstFrameIndex != plan.FrameIndex
                || frame.PaletteIndex != plan.PaletteIndex || frame.TextureIndex != plan.TextureIndex)
                throw new InvalidDataException($"HUD addition {index} mapping failed semantic verification.");
            if (reread.Palettes[plan.PaletteIndex].BankIndex != plan.Edit.BankIndex
                || reread.Textures[plan.TextureIndex].BankIndex != plan.Edit.BankIndex)
                throw new InvalidDataException($"HUD addition {index} bank routing failed semantic verification.");
            VerifyTexture(
                reread.Palettes[plan.PaletteIndex],
                reread.Textures[plan.TextureIndex],
                plan.Edit.Texture,
                $"HUD addition {index}");
        }
    }

    private static void VerifyTexture(
        HudPaletteEntry palette,
        HudTextureEntry texture,
        HudIndexedTexture expected,
        string label)
    {
        if (texture.Width != expected.Width || texture.Height != expected.Height
            || !palette.PaletteBytes.AsSpan().SequenceEqual(expected.PaletteBytes.Span)
            || !texture.PixelBytes.AsSpan().SequenceEqual(expected.PixelBytes.Span))
            throw new InvalidDataException($"{label} failed semantic verification.");
    }

    private static void WriteCumulativeCounts(
        byte[] header,
        int offset,
        IReadOnlyList<int> sourceCounts,
        IReadOnlyList<PlannedReplacement> replacements,
        IReadOnlyList<PlannedAddition> additions)
    {
        for (var bank = 0; bank < HudBankReader.BankCount; bank++)
        {
            var added = replacements.Count(replacement => replacement.BankIndex <= bank)
                + additions.Count(addition => addition.Edit.BankIndex <= bank);
            BinaryPrimitives.WriteInt32LittleEndian(
                header.AsSpan(offset + bank * 4, 4), checked(sourceCounts[bank] + added));
        }
    }

    private static void WritePaletteRecord(Span<byte> record, int offset, bool preserveMetadata)
    {
        var gsRam = preserveMetadata ? BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) : (ushort)0;
        var padding = preserveMetadata ? BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) : (ushort)0;
        record.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x80000000u | checked((uint)offset));
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], gsRam);
        BinaryPrimitives.WriteUInt16LittleEndian(record[6..], padding);
    }

    private static void WriteTextureRecord(
        Span<byte> record,
        int offset,
        int width,
        int height,
        bool preserveGsRam)
    {
        var gsRam = preserveGsRam ? BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) : (ushort)0;
        record.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x80000000u | checked((uint)offset));
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], gsRam);
        record[6] = checked((byte)Log2(width));
        record[7] = checked((byte)Log2(height));
    }

    private static int PlanPayload(
        long[] plannedLengths,
        int bank,
        int alignment,
        int length,
        string label,
        HudBankCompositionOptions options)
    {
        var offset = Align(plannedLengths[bank], alignment);
        var end = checked(offset + length);
        var capacity = BankCapacity(options, bank);
        if (end > capacity || end > HudBankReader.SerializedPayloadOffsetMask)
        {
            throw new InvalidDataException(
                $"{label} in bank {bank} requires 0x{end:X} bytes; capacity is 0x{capacity:X}.");
        }
        plannedLengths[bank] = end;
        return checked((int)offset);
    }

    private static void ValidateTexture(HudIndexedTexture texture, string label)
    {
        if (texture.Width <= 0 || texture.Height <= 0
            || !BitOperations.IsPow2((uint)texture.Width) || !BitOperations.IsPow2((uint)texture.Height))
            throw new InvalidDataException($"{label} dimensions {texture.Width}x{texture.Height} must be positive powers of two.");
        if (texture.PaletteBytes.Length != HudBankReader.PaletteLength)
            throw new InvalidDataException($"{label} palette must contain exactly 0x{HudBankReader.PaletteLength:X} bytes.");
        var expectedLength = checked((long)texture.Width * texture.Height);
        if (expectedLength != texture.PixelBytes.Length || expectedLength > int.MaxValue)
            throw new InvalidDataException(
                $"{label} pixel length {texture.PixelBytes.Length} does not match {texture.Width}x{texture.Height} indexed-8 pixels.");
        if (Log2(texture.Width) > 30 || Log2(texture.Height) > 30)
            throw new InvalidDataException($"{label} dimensions exceed the supported texture logarithm.");
    }

    private static void ValidateOptions(HudBankCompositionOptions options)
    {
        if (options.MaximumHeaderBytes < HudBankReader.HeaderFixedLength)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum HUD header size is too small.");
        if (options.MaximumDecompressedBankBytes < 0 || options.MaximumCompressedBankBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "HUD bank size limits cannot be negative.");
        if (options.BankCapacities is { } capacities)
        {
            if (capacities.Count != HudBankReader.BankCount)
                throw new ArgumentException($"HUD bank capacities must contain {HudBankReader.BankCount} values.", nameof(options));
            if (capacities.Any(capacity => capacity < 0 || capacity > options.MaximumDecompressedBankBytes))
                throw new ArgumentOutOfRangeException(nameof(options), "HUD bank capacity is outside the configured decompressed limit.");
        }
    }

    private static int BankCapacity(HudBankCompositionOptions options, int bank) =>
        options.BankCapacities?[bank] ?? options.MaximumDecompressedBankBytes;

    private static int LastPopulatedBank(IReadOnlyList<int> cumulativeCounts)
    {
        var previous = 0;
        var result = 0;
        for (var bank = 0; bank < cumulativeCounts.Count; bank++)
        {
            if (cumulativeCounts[bank] > previous) result = bank;
            previous = cumulativeCounts[bank];
        }
        return result;
    }

    private static int Log2(int value) => BitOperations.Log2((uint)value);

    private static long Align(long value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
