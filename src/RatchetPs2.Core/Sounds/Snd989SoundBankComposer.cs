using System.Buffers.Binary;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Sounds;

public static class Snd989SoundBankComposer
{
    private const int ContainerHeaderSize = 24;
    private const int BlockHeaderSize = 0x40;
    private const int ProgramRecordSize = 12;
    private const int GrainRecordSize = 8;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private const int MaximumSpuBytes = 2 * 1024 * 1024;
    private const int SectorSize = 2048;

    public static SoundBankCompositionResult Compose(
        ReadOnlySpan<byte> sourceBytes,
        IReadOnlyList<SoundBankProgramInput> programs,
        IReadOnlyList<SoundBankSampleInput> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(samples);
        var source = Snd989SoundBankReader.Inspect(sourceBytes, cancellationToken);
        if (programs.Count > short.MaxValue || samples.Count > short.MaxValue)
            throw new InvalidDataException("989snd composition exceeds signed 16-bit table counts.");
        if (ReadUInt32LittleEndian(sourceBytes, ContainerHeaderSize + 0x38) != 0
            || ReadUInt32LittleEndian(sourceBytes, ContainerHeaderSize + 0x3c) != 0)
            throw new NotSupportedException(
                "989snd composition does not support relocating optional names or per-sound user data.");

        _ = Unique(programs, value => value.Key, "program");
        _ = Unique(samples, value => value.Key, "sample");
        var programIndexes = programs.Select((value, index) => (value.Key, index))
            .ToDictionary(value => value.Key, value => value.index, StringComparer.Ordinal);
        var sampleOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        using var sampleOutput = new MemoryStream();
        var sourceSampleData = sourceBytes.Slice(
            source.SampleDataProvenance.Offset, source.SampleDataProvenance.Length);
        sampleOutput.Write(sourceSampleData);
        if (sampleOutput.Length > MaximumSpuBytes)
            throw new InvalidDataException(
                $"989snd source allocation exceeds the {MaximumSpuBytes}-byte PS2 SPU budget.");
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sample.EncodedBytes is not { Length: > 0 } || sample.EncodedBytes.Length % 16 != 0)
                throw new InvalidDataException($"989snd sample {sample.Key} is not frame-aligned ADPCM.");
            SoundSampleInfo? sourceSample = null;
            foreach (var candidate in source.Samples)
                if (sourceBytes.Slice(candidate.Provenance.Offset, candidate.Provenance.Length)
                    .SequenceEqual(sample.EncodedBytes))
                {
                    sourceSample = candidate;
                    break;
                }
            if (sourceSample is not null)
                sampleOffsets.Add(sample.Key, sourceSample.EncodedOffset);
            else
            {
                sampleOffsets.Add(sample.Key, checked((int)sampleOutput.Position));
                sampleOutput.Write(sample.EncodedBytes);
            }
            if (sampleOutput.Length > MaximumSpuBytes)
                throw new InvalidDataException(
                    $"989snd sample allocation exceeds the {MaximumSpuBytes}-byte PS2 SPU budget.");
        }

        if (programs.Any(value => value.Operations is null
                || value.Operations.Any(operation => operation is null)))
            throw new InvalidDataException("989snd composition contains a null operation list or entry.");
        var grainCount = programs.Sum(value => value.Operations.Count);
        if (grainCount > short.MaxValue || programs.Any(value => value.Operations.Count > sbyte.MaxValue))
            throw new InvalidDataException("989snd composition exceeds native grain-count bounds.");
        var programsOffset = BlockHeaderSize;
        var grainsOffset = checked(programsOffset + programs.Count * ProgramRecordSize);
        var grainDataOffset = checked(grainsOffset + grainCount * GrainRecordSize);
        using var metadata = new MemoryStream();
        metadata.Write(new byte[grainDataOffset]);
        sourceBytes.Slice(ContainerHeaderSize, BlockHeaderSize).CopyTo(metadata.GetBuffer());
        var sourceMetadata = sourceBytes.Slice(
            source.MetadataProvenance.Offset, source.MetadataProvenance.Length);
        var sourceGrainDataOffset = checked((int)ReadUInt32LittleEndian(sourceMetadata, 0x34));
        metadata.Write(sourceMetadata[sourceGrainDataOffset..]);
        var grainIndex = 0;
        foreach (var program in programs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateKey(program.Key, "program");
            var programOffset = programsOffset + programIndexes[program.Key] * ProgramRecordSize;
            var buffer = metadata.GetBuffer();
            buffer[programOffset] = unchecked((byte)program.Volume);
            buffer[programOffset + 1] = unchecked((byte)program.VolumeGroup);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(programOffset + 2, 2), program.Pan);
            buffer[programOffset + 4] = unchecked((byte)(sbyte)program.Operations.Count);
            buffer[programOffset + 5] = unchecked((byte)program.InstanceLimit);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(programOffset + 6, 2), program.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(
                buffer.AsSpan(programOffset + 8, 4), checked((uint)(grainIndex * GrainRecordSize)));
            foreach (var operation in program.Operations)
            {
                var grainOffset = grainsOffset + grainIndex * GrainRecordSize;
                var opcode = BuildOpcode(
                    metadata, grainDataOffset, operation, programIndexes, sampleOffsets);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    metadata.GetBuffer().AsSpan(grainOffset, 4), opcode);
                BinaryPrimitives.WriteInt32LittleEndian(
                    metadata.GetBuffer().AsSpan(grainOffset + 4, 4), operation.Delay);
                grainIndex++;
            }
        }
        if (metadata.Length > MaximumMetadataBytes)
            throw new InvalidDataException("989snd metadata exceeds the supported composition limit.");

        var metadataBytes = metadata.ToArray();
        BinaryPrimitives.WriteInt16LittleEndian(metadataBytes.AsSpan(0x16, 2), checked((short)programs.Count));
        BinaryPrimitives.WriteInt16LittleEndian(metadataBytes.AsSpan(0x18, 2), checked((short)grainCount));
        BinaryPrimitives.WriteInt16LittleEndian(metadataBytes.AsSpan(0x1a, 2), checked((short)samples.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x1c, 4), checked((uint)programsOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x20, 4), checked((uint)grainsOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x28, 4), checked((uint)sampleOutput.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x2c, 4), checked((uint)sampleOutput.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x30, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x34, 4), checked((uint)grainDataOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x38, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(metadataBytes.AsSpan(0x3c, 4), 0);

        using var output = new MemoryStream();
        output.Write(new byte[ContainerHeaderSize]);
        output.Write(metadataBytes);
        output.Write(sampleOutput.GetBuffer().AsSpan(0, checked((int)sampleOutput.Length)));
        var alignedLength = checked((int)((output.Length + SectorSize - 1) / SectorSize * SectorSize));
        output.SetLength(alignedLength);
        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), ContainerHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), checked((uint)metadataBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(16, 4), checked((uint)(ContainerHeaderSize + metadataBytes.Length)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), checked((uint)sampleOutput.Length));

        var inspected = Snd989SoundBankReader.Inspect(bytes, cancellationToken);
        if (inspected.Programs.Count != programs.Count || inspected.Samples.Count != samples.Count)
            throw new InvalidDataException("989snd composition failed semantic re-read validation.");
        return new(bytes, programs.Select((value, index) => new SoundBankProgramSlot(value.Key, index)).ToArray());
    }

    private static uint BuildOpcode(
        MemoryStream metadata,
        int grainDataOffset,
        SoundBankOperationInput operation,
        IReadOnlyDictionary<string, int> programIndexes,
        IReadOnlyDictionary<string, int> sampleOffsets)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.ParameterBytes is null)
            throw new InvalidDataException($"989snd operation 0x{operation.Type:x2} has no parameter payload.");
        var parameterLength = operation.Type switch
        {
            1 or 9 => 24,
            2 or 3 or 4 => 16,
            5 or 6 or 7 or 8 => 32,
            _ => 0,
        };
        if (parameterLength == 0)
        {
            if (operation.Tone is not null || operation.ParameterBytes.Length != 0
                || operation.ReferencedProgramKey is not null)
                throw new InvalidDataException($"989snd operation 0x{operation.Type:x2} has unexpected parameters.");
            return (uint)operation.Type << 24
                | (uint)(byte)operation.Argument2 << 16
                | (uint)(byte)operation.Argument1 << 8
                | (byte)operation.Argument0;
        }

        if (operation.ParameterBytes.Length != parameterLength)
            throw new InvalidDataException(
                $"989snd operation 0x{operation.Type:x2} requires {parameterLength} parameter bytes.");
        var parameters = operation.ParameterBytes.ToArray();
        if (operation.Type is 1 or 9)
            WriteTone(parameters, operation.Tone
                ?? throw new InvalidDataException("989snd tone operation is missing typed parameters."), sampleOffsets);
        else if (operation.Tone is not null)
            throw new InvalidDataException($"989snd operation 0x{operation.Type:x2} cannot contain a tone.");
        if (operation.ReferencedProgramKey is { } target)
        {
            if (!programIndexes.TryGetValue(target, out var targetIndex))
                throw new InvalidDataException($"989snd operation references missing program {target}.");
            if (operation.Type is 2 or 3)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(0, 4), 0x00574144);
                BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(4, 4), targetIndex);
            }
            else if (operation.Type is 5 or 6 or 8)
                BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(0x0c, 4), targetIndex);
            else
                throw new InvalidDataException(
                    $"989snd operation 0x{operation.Type:x2} cannot reference another program.");
        }
        var relativeOffset = checked((int)metadata.Position - grainDataOffset);
        if ((uint)relativeOffset > 0x00ffffff)
            throw new InvalidDataException("989snd grain parameter data exceeds its 24-bit offset field.");
        metadata.Write(parameters);
        return (uint)operation.Type << 24 | (uint)relativeOffset;
    }

    private static void WriteTone(
        Span<byte> bytes,
        SoundBankToneInput tone,
        IReadOnlyDictionary<string, int> sampleOffsets)
    {
        if (!sampleOffsets.TryGetValue(tone.SampleKey, out var sampleOffset))
            throw new InvalidDataException($"989snd tone references missing sample {tone.SampleKey}.");
        bytes[0] = unchecked((byte)tone.Priority);
        bytes[1] = unchecked((byte)tone.Volume);
        bytes[2] = unchecked((byte)tone.CenterNote);
        bytes[3] = unchecked((byte)tone.CenterFine);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[4..6], tone.Pan);
        bytes[6] = unchecked((byte)tone.NoteLow);
        bytes[7] = unchecked((byte)tone.NoteHigh);
        bytes[8] = unchecked((byte)tone.PitchBendLow);
        bytes[9] = unchecked((byte)tone.PitchBendHigh);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[0x0a..0x0c], tone.Adsr1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[0x0c..0x0e], tone.Adsr2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[0x0e..0x10], tone.Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[0x10..0x14], checked((uint)sampleOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[0x14..0x18], tone.Reserved);
    }

    private static Dictionary<string, T> Unique<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        string description)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null)
                throw new InvalidDataException($"989snd composition contains a null {description}.");
            var current = key(value);
            ValidateKey(current, description);
            if (!result.TryAdd(current, value))
                throw new InvalidDataException($"989snd composition contains duplicate {description} key {current}.");
        }
        return result;
    }

    private static void ValidateKey(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"989snd composition contains an empty {description} key.");
    }
}
