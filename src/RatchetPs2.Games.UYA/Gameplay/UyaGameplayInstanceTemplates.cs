using System.Buffers.Binary;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaGameplayInstanceTemplates
{
    public const int DefaultTieDrawDistance = 4_000;
    public const float DefaultShrubDrawDistance = 128f;

    public static byte[] CreateTie(int classId, int identifier)
    {
        ValidateClassId(classId);
        if (identifier < 0) throw new ArgumentOutOfRangeException(nameof(identifier));
        var bytes = new byte[UyaTieInstancesReader.RecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, classId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), DefaultTieDrawDistance);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(UyaTieInstancesReader.OcclusionIdOffset), identifier);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x4c), 0.01f);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x54), identifier);
        return bytes;
    }

    public static byte[] CreateShrub(int classId)
    {
        ValidateClassId(classId);
        var bytes = new byte[UyaShrubInstancesReader.RecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, classId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), DefaultShrubDrawDistance);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x4c), 0.01f);
        WriteNeutralRgb(bytes, 0x50);
        return bytes;
    }

    public static byte[] CreateMoby(int classId, int uid)
    {
        ValidateClassId(classId);
        if (uid < 0) throw new ArgumentOutOfRangeException(nameof(uid));
        var bytes = new byte[UyaMobyInstancesReader.RecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, UyaMobyInstancesReader.RecordSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), uid);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x28), classId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x2c), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x30), 1_024);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x34), 1_024);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x38), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x58), -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x68), -1);
        WriteNeutralRgb(bytes, 0x74);
        return bytes;
    }

    public static byte[] CreateNeutralTieAmbient(int size)
    {
        if (size < 0 || size % sizeof(ushort) != 0)
            throw new InvalidDataException("UYA tie ambient size must be a non-negative whole number of words.");
        var bytes = new byte[size];
        if (size >= 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x8080);
        if (size >= 4) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 0x0080);
        return bytes;
    }

    public static byte[] NormalizePlacedTie(ReadOnlySpan<byte> source)
    {
        RequireSize(source, UyaTieInstancesReader.RecordSize, "tie");
        if (BinaryPrimitives.ReadInt32LittleEndian(source[4..])
            != BitConverter.SingleToInt32Bits(DefaultTieDrawDistance)) return source.ToArray();
        var normalized = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(normalized.AsSpan(4), DefaultTieDrawDistance);
        return normalized;
    }

    public static byte[] NormalizePlacedShrub(ReadOnlySpan<byte> source)
    {
        RequireSize(source, UyaShrubInstancesReader.RecordSize, "shrub");
        var normalized = NormalizePlacedRgb96(source, 0x50);
        var repairDrawDistance = BinaryPrimitives.ReadSingleLittleEndian(normalized.AsSpan(4)) == 1_024;
        var repairHomogeneousScale = BinaryPrimitives.ReadSingleLittleEndian(normalized.AsSpan(0x4c)) == 0;
        if (repairDrawDistance)
            BinaryPrimitives.WriteSingleLittleEndian(normalized.AsSpan(4), DefaultShrubDrawDistance);
        if (repairHomogeneousScale)
            BinaryPrimitives.WriteSingleLittleEndian(normalized.AsSpan(0x4c), 0.01f);
        return normalized;
    }

    public static byte[] NormalizePlacedMoby(ReadOnlySpan<byte> source)
    {
        RequireSize(source, UyaMobyInstancesReader.RecordSize, "moby");
        return NormalizePlacedRgb96(source, 0x74);
    }

    private static byte[] NormalizePlacedRgb96(ReadOnlySpan<byte> source, int offset)
    {
        var normalized = source.ToArray();
        for (var index = 0; index < 3; index++)
            if (BinaryPrimitives.ReadInt32LittleEndian(source[(offset + index * 4)..]) != 0x3f800000)
                return normalized;
        WriteNeutralRgb(normalized, offset);
        return normalized;
    }

    private static void WriteNeutralRgb(Span<byte> bytes, int offset)
    {
        for (var index = 0; index < 3; index++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(offset + index * 4)..], 255);
    }

    private static void RequireSize(ReadOnlySpan<byte> bytes, int expected, string family)
    {
        if (bytes.Length != expected)
            throw new InvalidDataException($"UYA {family} instance must be 0x{expected:X} bytes.");
    }

    private static void ValidateClassId(int classId)
    {
        if (classId < 0) throw new ArgumentOutOfRangeException(nameof(classId));
    }
}
