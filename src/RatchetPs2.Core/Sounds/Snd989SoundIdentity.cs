using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RatchetPs2.Core.Sounds;

internal static class Snd989SoundIdentity
{
    // These domains are persisted through Forge asset IDs. Keep their original values across refactors.
    private const string SampleIdentityDomainV1 = "uya-989snd-sample-v1";
    private const string DirectProgramIdentityDomainV1 = "uya-989snd-program-v1";
    private const string ProgramIdentityDomainV3 = "uya-989snd-program-v3";

    public static string HashSample(ReadOnlySpan<byte> encoded)
    {
        using var canonical = new MemoryStream(encoded.Length + 64);
        AppendField(canonical, SampleIdentityDomainV1);
        AppendField(canonical, "ps2-spu-adpcm");
        AppendField(canonical, encoded);
        return Hash(canonical);
    }

    public static string HashProgram(
        sbyte volume,
        sbyte volumeGroup,
        short pan,
        sbyte instanceLimit,
        ushort flags,
        IReadOnlyList<SoundOperationInfo> operations,
        Func<int, string> referencedProgramId)
    {
        if (operations.Count == 1 && operations[0] is { Type: 1, Tone: not null })
            return HashDirectProgram(volume, volumeGroup, pan, instanceLimit, flags, operations[0]);
        using var canonical = new MemoryStream(256);
        AppendField(canonical, ProgramIdentityDomainV3);
        AppendField(canonical, 3);
        AppendField(canonical, volume);
        AppendField(canonical, volumeGroup);
        AppendField(canonical, pan);
        AppendField(canonical, instanceLimit);
        AppendField(canonical, flags);
        AppendField(canonical, operations.Count);
        foreach (var operation in operations)
        {
            AppendField(canonical, operation.Type);
            AppendField(canonical, operation.Delay);
            if (operation.Tone is { } operationTone)
            {
                AppendField(canonical, (byte)1);
                AppendTone(canonical, operationTone);
            }
            else
            {
                AppendField(canonical, (byte)0);
                if (operation.ParameterBytes.Length > 0)
                {
                    var parameters = operation.ParameterBytes;
                    if (operation.ReferencedProgramIndex is { } target)
                    {
                        parameters = parameters.ToArray();
                        if (operation.Type is 2 or 3) parameters.AsSpan(4, 4).Clear();
                        else if (operation.Type is 5 or 6 or 8) parameters.AsSpan(0x0c, 4).Clear();
                        AppendField(canonical, parameters);
                        AppendField(canonical, referencedProgramId(target));
                    }
                    else
                        AppendField(canonical, parameters);
                }
                else
                {
                    AppendField(canonical, operation.Argument0);
                    AppendField(canonical, operation.Argument1);
                    AppendField(canonical, operation.Argument2);
                }
            }
        }
        return Hash(canonical);
    }

    private static string HashDirectProgram(
        sbyte volume,
        sbyte volumeGroup,
        short pan,
        sbyte instanceLimit,
        ushort flags,
        SoundOperationInfo operation)
    {
        using var canonical = new MemoryStream(256);
        AppendField(canonical, DirectProgramIdentityDomainV1);
        AppendField(canonical, 3);
        AppendField(canonical, volume);
        AppendField(canonical, volumeGroup);
        AppendField(canonical, pan);
        AppendField(canonical, instanceLimit);
        AppendField(canonical, flags);
        AppendField(canonical, operation.Type);
        AppendField(canonical, operation.Delay);
        AppendTone(canonical, operation.Tone!);
        return Hash(canonical);
    }

    private static void AppendTone(Stream canonical, SoundToneInfo tone)
    {
        AppendField(canonical, tone.Priority);
        AppendField(canonical, tone.Volume);
        AppendField(canonical, tone.CenterNote);
        AppendField(canonical, tone.CenterFine);
        AppendField(canonical, tone.Pan);
        AppendField(canonical, tone.NoteLow);
        AppendField(canonical, tone.NoteHigh);
        AppendField(canonical, tone.PitchBendLow);
        AppendField(canonical, tone.PitchBendHigh);
        AppendField(canonical, tone.Adsr1);
        AppendField(canonical, tone.Adsr2);
        AppendField(canonical, tone.Flags);
        AppendField(canonical, tone.Reserved);
        AppendField(canonical, tone.SampleId);
    }

    private static string Hash(MemoryStream canonical) =>
        Convert.ToHexString(SHA256.HashData(
            canonical.GetBuffer().AsSpan(0, (int)canonical.Length))).ToLowerInvariant();

    private static void AppendField(Stream output, string value) =>
        AppendField(output, Encoding.UTF8.GetBytes(value));

    private static void AppendField(Stream output, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        output.Write(length);
        output.Write(value);
    }

    private static void AppendField(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        AppendField(output, bytes);
    }

    private static void AppendField(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AppendField(output, bytes);
    }

    private static void AppendField(Stream output, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        AppendField(output, bytes);
    }

    private static void AppendField(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        AppendField(output, bytes);
    }

    private static void AppendField(Stream output, sbyte value) =>
        AppendField(output, [(byte)value]);

    private static void AppendField(Stream output, byte value) => AppendField(output, [value]);
}
