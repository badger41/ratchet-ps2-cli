namespace RatchetPs2.Core.Sounds;

public sealed record SoundBankSampleInput(
    string Key,
    byte[] EncodedBytes);

public sealed record SoundBankToneInput(
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
    uint Reserved,
    string SampleKey);

public sealed record SoundBankOperationInput(
    byte Type,
    sbyte Argument0,
    sbyte Argument1,
    sbyte Argument2,
    int Delay,
    byte[] ParameterBytes,
    SoundBankToneInput? Tone,
    string? ReferencedProgramKey);

public sealed record SoundBankProgramInput(
    string Key,
    sbyte Volume,
    sbyte VolumeGroup,
    short Pan,
    sbyte InstanceLimit,
    ushort Flags,
    IReadOnlyList<SoundBankOperationInput> Operations);

public sealed record SoundBankProgramSlot(string Key, int NativeIndex);

public sealed record SoundBankCompositionResult(
    byte[] BankBytes,
    IReadOnlyList<SoundBankProgramSlot> ProgramSlots);

public sealed record SoundRemapComposition(
    int ProgramCount,
    IReadOnlyDictionary<int, IReadOnlyList<int>> MobySlotsByClassId,
    IReadOnlyList<int> GlobalSlots,
    byte[] OpaqueOtherRecords);
