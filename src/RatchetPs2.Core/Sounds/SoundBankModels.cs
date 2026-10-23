namespace RatchetPs2.Core.Sounds;

public readonly record struct SoundSourceRange(int Offset, int Length);

public sealed record SoundToneInfo(
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
    uint Reserved,
    string SampleId);

public sealed record SoundOperationInfo(
    int NativeIndex,
    byte Type,
    sbyte Argument0,
    sbyte Argument1,
    sbyte Argument2,
    int Delay,
    SoundSourceRange Provenance,
    SoundSourceRange? ParameterProvenance,
    byte[] ParameterBytes,
    SoundToneInfo? Tone,
    int? ReferencedProgramIndex);

public sealed record SoundProgramInfo(
    int NativeIndex,
    string? ContentId,
    sbyte Volume,
    sbyte VolumeGroup,
    short Pan,
    sbyte InstanceLimit,
    ushort Flags,
    SoundSourceRange Provenance,
    IReadOnlyList<SoundOperationInfo> Operations,
    IReadOnlyList<string> SampleIds,
    bool PreviewSupported,
    string? PreviewBlocker);

public sealed record SoundSampleInfo(
    string ContentId,
    int EncodedOffset,
    int EncodedLength,
    int DecodedFrameCount,
    bool IsLooping,
    int? LoopStartFrame,
    int LoopEndFrame,
    SoundSourceRange Provenance);

public sealed record SoundBankInventory(
    string Format,
    int FormatVersion,
    int BankId,
    SoundSourceRange MetadataProvenance,
    SoundSourceRange SampleDataProvenance,
    IReadOnlyList<SoundProgramInfo> Programs,
    IReadOnlyList<SoundSampleInfo> Samples);

public sealed record SoundRemapSlotInfo(
    int Slot,
    int NativeProgramIndex,
    SoundSourceRange Provenance);

public sealed record SoundMobyRemapInfo(
    int DefinitionIndex,
    int ClassId,
    short RawListOffset,
    short RawSlotCount,
    SoundSourceRange ConfigurationProvenance,
    SoundSourceRange? SlotProvenance,
    IReadOnlyList<SoundRemapSlotInfo> Slots);

public sealed record SoundRemapInventory(
    SoundSourceRange Provenance,
    IReadOnlyList<SoundMobyRemapInfo> Mobys,
    int OtherRecordCount,
    SoundSourceRange OtherRecordsProvenance,
    IReadOnlyList<SoundRemapSlotInfo> Globals,
    SoundSourceRange GlobalProvenance);

public sealed record SoundPcmPreview(
    int ProgramIndex,
    string ProgramId,
    string SampleId,
    int SampleRate,
    int Channels,
    short[] Pcm16,
    bool IsLooping,
    int? LoopStartFrame,
    int LoopEndFrame,
    float Gain,
    int PanDegrees,
    bool IsVariable);
