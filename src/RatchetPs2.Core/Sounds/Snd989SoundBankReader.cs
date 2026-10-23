using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Sounds;

public static partial class Snd989SoundBankReader
{
    private const int MaximumBankBytes = 64 * 1024 * 1024;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private const int MaximumSampleBytes = 32 * 1024 * 1024;
    private const int MaximumPreviewFrames = 8_000_000;
    private const int MaximumGraphDepth = 128;
    private const uint Sblk = 0x6b6c4253;
    private const uint DawBlockId = 0x00574144;

    private sealed record ParsedTone(
        sbyte Priority,
        sbyte Volume,
        sbyte CenterNote,
        sbyte CenterFine,
        short Pan,
        sbyte NoteLow,
        sbyte NoteHigh,
        sbyte PitchBendLow,
        sbyte PitchBendHigh,
        ushort Adsr1,
        ushort Adsr2,
        ushort Flags,
        int SampleOffset,
        uint Reserved);

    private sealed record ParsedOperation(
        int NativeIndex,
        byte Type,
        sbyte Argument0,
        sbyte Argument1,
        sbyte Argument2,
        int Delay,
        SoundSourceRange Provenance,
        SoundSourceRange? ParameterProvenance,
        byte[] ParameterBytes,
        ParsedTone? Tone,
        int? ReferencedProgramIndex);

    public static SoundBankInventory Inspect(
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length is < 24 or > MaximumBankBytes || bytes.Length % 2048 != 0)
            throw new InvalidDataException("989snd bank size or sector alignment is invalid.");
        if (ReadUInt32LittleEndian(bytes, 0) != 3 || ReadUInt32LittleEndian(bytes, 4) != 2)
            throw new InvalidDataException("989snd bank requires a type-3 two-chunk container.");

        var metadataOffset = ToInt32(ReadUInt32LittleEndian(bytes, 8), "metadata offset");
        var metadataLength = ToInt32(ReadUInt32LittleEndian(bytes, 12), "metadata size");
        var sampleDataOffset = ToInt32(ReadUInt32LittleEndian(bytes, 16), "sample offset");
        var sampleDataLength = ToInt32(ReadUInt32LittleEndian(bytes, 20), "sample size");
        if (metadataOffset != 24 || metadataLength > MaximumMetadataBytes
            || sampleDataLength > MaximumSampleBytes || sampleDataLength % 16 != 0
            || checked(metadataOffset + metadataLength) != sampleDataOffset)
            throw new InvalidDataException("989snd bank chunk layout is invalid.");
        var sampleDataEnd = checked(sampleDataOffset + sampleDataLength);
        var metadata = Slice(bytes, metadataOffset, metadataLength, "metadata");
        var sampleData = Slice(bytes, sampleDataOffset, sampleDataLength, "sample data");
        for (var offset = sampleDataEnd; offset < bytes.Length; offset++)
            if (bytes[offset] != 0)
                throw new InvalidDataException("989snd bank sector padding is not zero.");

        if (metadata.Length < 0x40 || ReadUInt32LittleEndian(metadata, 0) != Sblk
            || ReadUInt32LittleEndian(metadata, 4) != 3 || ReadUInt32LittleEndian(metadata, 8) != 4
            || ReadUInt32LittleEndian(metadata, 0x0c) != DawBlockId)
            throw new InvalidDataException("989snd SBlk header is unsupported.");
        var programCount = ReadNonNegativeInt16(metadata, 0x16, "program count");
        var grainCount = ReadNonNegativeInt16(metadata, 0x18, "grain count");
        var declaredSampleCount = ReadNonNegativeInt16(metadata, 0x1a, "sample count");
        var programsOffset = ToInt32(ReadUInt32LittleEndian(metadata, 0x1c), "program table offset");
        var grainsOffset = ToInt32(ReadUInt32LittleEndian(metadata, 0x20), "grain table offset");
        var grainDataOffset = ToInt32(ReadUInt32LittleEndian(metadata, 0x34), "grain data offset");
        if (ReadUInt32LittleEndian(metadata, 0x28) != sampleDataLength
            || ReadUInt32LittleEndian(metadata, 0x2c) < sampleDataLength
            || programsOffset != 0x40
            || checked(programsOffset + programCount * 12) != grainsOffset
            || checked(grainsOffset + grainCount * 8) != grainDataOffset)
            throw new InvalidDataException("989snd bank table layout is invalid.");
        _ = Slice(metadata, programsOffset, checked(programCount * 12), "program table");
        _ = Slice(metadata, grainsOffset, checked(grainCount * 8), "grain table");

        var operations = ReadOperations(
            metadata, metadataOffset, grainsOffset, grainDataOffset, grainCount,
            programCount, unchecked((int)DawBlockId), cancellationToken);
        var samples = ReadSamples(
            sampleData,
            sampleDataOffset,
            operations.Where(value => value.Tone is not null)
                .Select(value => value.Tone!.SampleOffset)
                .Distinct()
                .Order()
                .ToArray(),
            declaredSampleCount,
            cancellationToken);
        var samplesByOffset = samples.ToDictionary(value => value.EncodedOffset);
        var publicOperations = operations.Select(operation => ToPublic(operation, samplesByOffset)).ToArray();

        var programs = new SoundProgramInfo[programCount];
        for (var index = 0; index < programCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = programsOffset + index * 12;
            var count = unchecked((sbyte)metadata[offset + 4]);
            var firstOffset = ToInt32(
                ReadUInt32LittleEndian(metadata, offset + 8), $"program {index} first grain");
            if (count < 0 || firstOffset % 8 != 0 || firstOffset / 8 > grainCount - count)
                throw new InvalidDataException($"989snd program {index} grain window is invalid.");
            var firstIndex = firstOffset / 8;
            var programOperations = publicOperations.Skip(firstIndex).Take(count).ToArray();
            var sampleIds = programOperations
                .Where(value => value.Tone is not null)
                .Select(value => value.Tone!.SampleId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var volume = unchecked((sbyte)metadata[offset]);
            var volumeGroup = unchecked((sbyte)metadata[offset + 1]);
            var pan = ReadInt16LittleEndian(metadata, offset + 2);
            var instanceLimit = unchecked((sbyte)metadata[offset + 5]);
            var flags = ReadUInt16LittleEndian(metadata, offset + 6);
            var blocker = PreviewBlocker(programOperations);
            programs[index] = new(
                index,
                null,
                volume,
                volumeGroup,
                pan,
                instanceLimit,
                flags,
                new(metadataOffset + offset, 12),
                programOperations,
                sampleIds,
                blocker is null,
                blocker);
        }
        ValidateGraph(programs);
        var resolving = new bool[programs.Length];
        for (var index = 0; index < programs.Length; index++) ResolveIdentity(index);

        return new(
            "989snd-sblk",
            3,
            unchecked((int)DawBlockId),
            new(metadataOffset, metadataLength),
            new(sampleDataOffset, sampleDataLength),
            programs,
            samples);

        string? ResolveIdentity(int index)
        {
            var program = programs[index];
            if (program.ContentId is not null || !program.PreviewSupported) return program.ContentId;
            if (resolving[index]) throw new InvalidDataException("989snd dependency graph contains a cycle.");
            resolving[index] = true;
            foreach (var operation in program.Operations)
            {
                if (operation.ReferencedProgramIndex is not { } target) continue;
                if (ResolveIdentity(target) is not null) continue;
                var blocker = $"Referenced program {target} is not importable: {programs[target].PreviewBlocker}";
                programs[index] = program with
                {
                    PreviewSupported = false,
                    PreviewBlocker = blocker,
                };
                resolving[index] = false;
                return null;
            }
            var contentId = Snd989SoundIdentity.HashProgram(
                program.Volume, program.VolumeGroup, program.Pan, program.InstanceLimit,
                program.Flags, program.Operations,
                target => programs[target].ContentId
                    ?? throw new InvalidDataException("989snd dependency identity is unresolved."));
            programs[index] = program with { ContentId = contentId };
            resolving[index] = false;
            return contentId;
        }
    }

    private static ParsedOperation[] ReadOperations(
        ReadOnlySpan<byte> metadata,
        int metadataOffset,
        int grainsOffset,
        int grainDataOffset,
        int grainCount,
        int programCount,
        int blockId,
        CancellationToken cancellationToken)
    {
        var result = new ParsedOperation[grainCount];
        for (var index = 0; index < grainCount; index++)
        {
            if ((index & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = grainsOffset + index * 8;
            var opcode = ReadUInt32LittleEndian(metadata, offset);
            var type = (byte)(opcode >> 24);
            var parameterLength = type switch
            {
                1 or 9 => 24,
                2 or 3 or 4 => 16,
                5 or 6 or 7 or 8 => 32,
                _ => 0,
            };
            var parameterOffset = 0;
            byte[] parameterBytes = [];
            SoundSourceRange? parameterProvenance = null;
            if (parameterLength > 0)
            {
                parameterOffset = checked(grainDataOffset + (int)(opcode & 0x00ffffff));
                parameterBytes = Slice(
                    metadata, parameterOffset, parameterLength, $"grain {index} parameters").ToArray();
                parameterProvenance = new(metadataOffset + parameterOffset, parameterLength);
            }
            var tone = type is 1 or 9 ? ReadTone(parameterBytes, index) : null;
            var referencedProgram = ReadProgramReference(type, parameterBytes, blockId);
            if (referencedProgram is < 0 || referencedProgram >= programCount)
                throw new InvalidDataException($"989snd grain {index} references an invalid program.");
            result[index] = new(
                index,
                type,
                unchecked((sbyte)(byte)opcode),
                unchecked((sbyte)(byte)(opcode >> 8)),
                unchecked((sbyte)(byte)(opcode >> 16)),
                ReadInt32LittleEndian(metadata, offset + 4),
                new(metadataOffset + offset, 8),
                parameterProvenance,
                parameterBytes,
                tone,
                referencedProgram);
        }
        return result;
    }

    private static ParsedTone ReadTone(ReadOnlySpan<byte> bytes, int grainIndex)
    {
        var sampleOffset = ToInt32(
            ReadUInt32LittleEndian(bytes, 0x10), $"grain {grainIndex} sample offset");
        return new(
            unchecked((sbyte)bytes[0]),
            unchecked((sbyte)bytes[1]),
            unchecked((sbyte)bytes[2]),
            unchecked((sbyte)bytes[3]),
            ReadInt16LittleEndian(bytes, 4),
            unchecked((sbyte)bytes[6]),
            unchecked((sbyte)bytes[7]),
            unchecked((sbyte)bytes[8]),
            unchecked((sbyte)bytes[9]),
            ReadUInt16LittleEndian(bytes, 0x0a),
            ReadUInt16LittleEndian(bytes, 0x0c),
            ReadUInt16LittleEndian(bytes, 0x0e),
            sampleOffset,
            ReadUInt32LittleEndian(bytes, 0x14));
    }

    private static int? ReadProgramReference(byte type, ReadOnlySpan<byte> parameters, int blockId)
    {
        if (type is 2 or 3)
        {
            if (unchecked((int)ReadUInt32LittleEndian(parameters, 0)) != blockId) return null;
            return ToInt32(ReadUInt32LittleEndian(parameters, 4), "referenced program index");
        }
        if (type is 5 or 6 or 8)
        {
            var value = ReadInt32LittleEndian(parameters, 0x0c);
            return value >= 0 ? value : null;
        }
        return null;
    }

    private static SoundSampleInfo[] ReadSamples(
        ReadOnlySpan<byte> sampleData,
        int sampleDataOffset,
        IReadOnlyList<int> starts,
        int declaredCount,
        CancellationToken cancellationToken)
    {
        if (starts.Count != declaredCount)
            throw new InvalidDataException("989snd sample identity count does not match the block header.");
        var result = new SoundSampleInfo[starts.Count];
        var previousEnd = 0;
        for (var index = 0; index < starts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = starts[index];
            if (start < 0 || start % 16 != 0 || start >= sampleData.Length || start < previousEnd)
                throw new InvalidDataException("989snd sample offset or range is invalid.");
            int? loopStart = null;
            var decodedFrames = 0;
            var end = -1;
            byte endFlags = 0;
            for (var offset = start; offset <= sampleData.Length - 16; offset += 16)
            {
                if (((offset - start) & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
                var header = sampleData[offset];
                if ((header >> 4) > 4 || (header & 0x0f) > 12)
                    throw new InvalidDataException("989snd sample has an invalid PS2 ADPCM frame header.");
                var flags = sampleData[offset + 1];
                if ((flags & 4) != 0)
                {
                    if (loopStart is not null)
                        throw new InvalidDataException("989snd sample has multiple loop-start frames.");
                    loopStart = decodedFrames;
                }
                decodedFrames = checked(decodedFrames + 28);
                if ((flags & 1) == 0) continue;
                end = offset + 16;
                endFlags = flags;
                break;
            }
            if (end < 0) throw new InvalidDataException("989snd sample has no terminating ADPCM frame.");
            var looping = (endFlags & 2) != 0;
            if (looping != (loopStart is not null))
                throw new InvalidDataException("989snd sample loop markers are inconsistent.");
            var encoded = sampleData[start..end];
            result[index] = new(
                Snd989SoundIdentity.HashSample(encoded),
                start,
                encoded.Length,
                decodedFrames,
                looping,
                loopStart,
                decodedFrames,
                new(sampleDataOffset + start, encoded.Length));
            previousEnd = end;
        }
        return result;
    }

    private static SoundOperationInfo ToPublic(
        ParsedOperation operation,
        IReadOnlyDictionary<int, SoundSampleInfo> samples)
    {
        SoundToneInfo? tone = null;
        if (operation.Tone is not null)
        {
            if (!samples.TryGetValue(operation.Tone.SampleOffset, out var sample))
                throw new InvalidDataException(
                    $"989snd grain {operation.NativeIndex} references a missing sample.");
            var value = operation.Tone;
            tone = new(
                value.Priority,
                value.Volume,
                value.CenterNote,
                value.CenterFine,
                value.Pan,
                value.NoteLow,
                value.NoteHigh,
                value.PitchBendLow,
                value.PitchBendHigh,
                value.Adsr1,
                value.Adsr2,
                value.Flags,
                value.SampleOffset,
                value.Reserved,
                sample.ContentId);
        }
        return new(
            operation.NativeIndex,
            operation.Type,
            operation.Argument0,
            operation.Argument1,
            operation.Argument2,
            operation.Delay,
            operation.Provenance,
            operation.ParameterProvenance,
            operation.ParameterBytes,
            tone,
            operation.ReferencedProgramIndex);
    }

    private static void ValidateGraph(IReadOnlyList<SoundProgramInfo> programs)
    {
        var state = new byte[programs.Count];
        for (var index = 0; index < programs.Count; index++) Visit(index, 0);

        void Visit(int index, int depth)
        {
            if (depth > MaximumGraphDepth)
                throw new InvalidDataException("989snd dependency graph exceeds the supported depth.");
            if (state[index] == 1)
                throw new InvalidDataException("989snd dependency graph contains a cycle.");
            if (state[index] == 2) return;
            state[index] = 1;
            foreach (var operation in programs[index].Operations)
                if (operation.ReferencedProgramIndex is { } target)
                    Visit(target, depth + 1);
            state[index] = 2;
        }
    }

    private static string? PreviewBlocker(IReadOnlyList<SoundOperationInfo> operations)
    {
        if (operations.Count == 0) return "Sound program contains no operations.";
        foreach (var operation in operations)
        {
            if (operation.Delay < 0) return "Negative operation delays are not supported for preview.";
            if (operation.Type is 1 or 9)
            {
                if (operation.Tone is not { } tone)
                    return $"Tone operation {operation.NativeIndex} has no tone parameters.";
                if (tone.Volume < -37 || tone.Pan < -37)
                    return $"Tone operation {operation.NativeIndex} uses an invalid register selector.";
                continue;
            }
            if (operation.Type is 2 or 3 or 5 or 6 or 8 && operation.ReferencedProgramIndex is null)
                return "Named cross-bank child sounds and branches are not supported for preview.";
            if (!SupportedPreviewOperation(operation.Type))
                return $"Operation type 0x{operation.Type:x2} is not supported for preview.";
            if (operation.Type == 4 && (operation.ParameterBytes[0] >= 4
                    || operation.ParameterBytes[1] > 6 || operation.ParameterBytes[3] > 5))
                return $"LFO operation {operation.NativeIndex} has unsupported parameters.";
            if (operation.Type is 0x19 or 0x27
                && (operation.Argument0 <= 0 || operation.Argument1 < 0))
                return $"Selection operation {operation.NativeIndex} has invalid bounds.";
            if (operation.Type == 0x1f && operation.Argument2 < operation.Argument1)
                return $"Random-register operation {operation.NativeIndex} has invalid bounds.";
            if (operation.Type == 0x25 && operation.Argument1 < operation.Argument0)
                return $"Random-marker operation {operation.NativeIndex} has invalid bounds.";
            if (operation.Type is >= 0x1e and <= 0x22 or 0x28 or 0x2c)
            {
                var first = operation.Type == 0x28 ? operation.Argument1 : operation.Argument0;
                if (!ValidRegister(first))
                    return $"Register operation {operation.NativeIndex} has an invalid selector.";
                if (operation.Type == 0x2c && !ValidRegister(operation.Argument1))
                    return $"Register operation {operation.NativeIndex} has an invalid destination.";
            }
        }
        return null;
    }

    private static bool SupportedPreviewOperation(byte type) => type is
        0 or 2 or 3 or 4 or 5 or 6 or 8
        or >= 0x14 and <= 0x2c;

    private static bool ValidRegister(sbyte value) => value is >= 0 and < 4 or >= -32 and < 0;

    private static int ReadNonNegativeInt16(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = ReadInt16LittleEndian(bytes, offset);
        return value >= 0 ? value : throw new InvalidDataException($"989snd {name} is negative.");
    }

    private static int ToInt32(uint value, string name) => value <= int.MaxValue
        ? (int)value
        : throw new InvalidDataException($"989snd {name} exceeds signed bounds.");
}
