using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Sounds;

public static partial class Snd989SoundBankReader
{
    public static SoundPcmPreview RenderPreview(
        byte[] bytes,
        int programIndex,
        int maximumFrames,
        bool preferAudible,
        CancellationToken cancellationToken = default)
    {
        if (maximumFrames is <= 0 or > MaximumPreviewFrames)
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrames), $"Preview frame budget must be from 1 to {MaximumPreviewFrames}.");
        var inventory = Inspect(bytes, cancellationToken);
        if ((uint)programIndex >= (uint)inventory.Programs.Count)
            throw new ArgumentOutOfRangeException(nameof(programIndex));
        var program = inventory.Programs[programIndex];
        if (!program.PreviewSupported)
            throw new NotSupportedException(program.PreviewBlocker);
        if (IsDirectPreview(program))
        {
            var tone = program.Operations[0].Tone!;
            var sample = inventory.Samples.Single(value => value.EncodedOffset == tone.SampleOffset);
            if (sample.DecodedFrameCount > maximumFrames)
                throw new InvalidDataException(
                    $"989snd preview requires {sample.DecodedFrameCount} frames, exceeding the {maximumFrames} frame budget.");
            var encoded = bytes.AsSpan(sample.Provenance.Offset, sample.Provenance.Length);
            var pcm = Ps2SpuAdpcmDecoder.Decode(encoded, sample.DecodedFrameCount, cancellationToken);
            return new(
                programIndex,
                program.ContentId!,
                sample.ContentId,
                PreviewSampleRate(tone.CenterNote, tone.CenterFine),
                1,
                pcm,
                sample.IsLooping,
                sample.LoopStartFrame,
                sample.LoopEndFrame,
                program.Volume / 127f * (tone.Volume / 127f),
                NormalizePan(program.Pan + tone.Pan),
                false);
        }
        var isVariable = HasVariablePreview(inventory, programIndex);
        var rendered = Snd989SoundPreviewRenderer.Render(
            bytes, inventory, programIndex, maximumFrames,
            preferAudible && isVariable, cancellationToken);
        return new(
            programIndex,
            program.ContentId!,
            program.SampleIds.FirstOrDefault() ?? program.ContentId!,
            Snd989SoundPreviewRenderer.SampleRate,
            2,
            rendered,
            false,
            null,
            rendered.Length / 2,
            1,
            0,
            isVariable);
    }

    private static bool IsDirectPreview(SoundProgramInfo program) =>
        program.Operations is [{ Type: 1, Delay: 0, Tone: { Volume: >= 0, Pan: >= 0 } tone }]
        && program.Volume >= 0
        && (tone.Flags & 0x18) == 0
        && PreviewSampleRate(tone.CenterNote, tone.CenterFine) is >= 1_000 and <= 192_000;

    private static int NormalizePan(int value)
    {
        value %= 360;
        return value < 0 ? value + 360 : value;
    }

    public static int PreviewSampleRate(sbyte centerNote, sbyte centerFine)
    {
        var baseRate = centerNote < 0 ? 48_000d : 44_100d;
        // sceSdNote2Pitch adds center fine to playback fine; it is not part of the subtracted center note.
        var semitones = (60 * 128 - Math.Abs((int)centerNote) * 128 + centerFine) / (12d * 128d);
        return checked((int)Math.Round(baseRate * Math.Pow(2d, semitones)));
    }

    private static bool HasVariablePreview(SoundBankInventory inventory, int programIndex)
    {
        var visited = new HashSet<int>();
        return Visit(programIndex);

        bool Visit(int index)
        {
            if (!visited.Add(index)) return false;
            foreach (var operation in inventory.Programs[index].Operations)
            {
                if (operation.Type is 0x19 or 0x1a or 0x1b or 0x1f or 0x25 or 0x27
                    || operation.Tone is { Volume: -5 } or { Pan: -5 }
                    || operation.Type == 4 && (operation.ParameterBytes[3] == 5
                        || (ReadUInt16LittleEndian(operation.ParameterBytes, 8) & 2) != 0)
                    || operation.Type == 5 && (ReadInt32LittleEndian(operation.ParameterBytes, 0) == -5
                        || ReadInt32LittleEndian(operation.ParameterBytes, 4) == -5))
                    return true;
                if (operation.ReferencedProgramIndex is { } child && operation.Type is 5 or 8 && Visit(child))
                    return true;
            }
            return false;
        }
    }
}
