using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class UyaSoundBankContractTests
{
    private const int MaximumBankBytes = 64 * 1024 * 1024;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private const int MaximumSampleBytes = 32 * 1024 * 1024;
    private const uint Sblk = 0x6b6c4253;

    private sealed record Evidence(
        string Sha256,
        int MetadataBytes,
        int SampleBytes,
        int Sounds,
        int Grains,
        int Samples,
        int MobyConfigs,
        int OtherRecords,
        int GlobalIds);

    private sealed record BankReport(
        int MetadataBytes,
        int SampleBytes,
        int Sounds,
        int Grains,
        int Samples,
        IReadOnlyList<byte> OpcodeTypes);

    private sealed record RemapReport(int MobyConfigs, int OtherRecords, int GlobalIds);

    private static readonly IReadOnlyDictionary<string, Evidence> Corpus =
        new Dictionary<string, Evidence>(StringComparer.Ordinal)
        {
            ["level01_iso_world01"] = new(
                "d50bf983cc038241d751125e602d16a53288e9b17ac7cfb88157a23ec9bdf82e",
                27740, 1583152, 321, 1107, 261, 227, 42, 256),
            ["level03_iso_world01"] = new(
                "75e5eea7a97dfa448cd073af57faa0a65d62ec1cc10a3d9508f6a7f209365ade",
                26980, 1549408, 311, 1078, 284, 275, 42, 232),
            ["level04_iso_world01"] = new(
                "fc91a26e17303ce80176c4e88c8aec114cd9f0d442ee8e14050f0a348737453d",
                27224, 1683568, 322, 1042, 276, 254, 45, 280),
            ["level08_iso_world01"] = new(
                "d085f2f371e375184b4edf2f2e71172f27a0ae821106743048d1007b40ad81b5",
                22232, 1622816, 284, 807, 242, 225, 44, 248),
            ["level14_iso_world01"] = new(
                "0e629977664acd4161b627358b91d4de4bb5c97fd4f68003f6db7306a83ece13",
                23948, 1566496, 305, 896, 256, 213, 46, 288),
            ["level17_iso_world01"] = new(
                "473d9edebc8f6f09d14540dff86ec8d3ee063991540d4117f92dfdd2786b458c",
                20768, 1364384, 254, 789, 230, 183, 41, 224),
            ["level21_iso_world01"] = new(
                "458f960271898412413ef424ee4818e0ad00fcce2dd4c1c133a52dac17735e3d",
                17548, 1290384, 229, 650, 211, 190, 46, 288),
            ["level28_iso_world01"] = new(
                "2ec3752bdfc8c4a3ac67568a2445446825f7132495acb31abae224f6e7a0cda1",
                17144, 1324240, 242, 618, 199, 175, 40, 216),
            ["level41_iso_world01"] = new(
                "c878f9b9a7b511bee88e373a4c9721af41a38c04d5277334988717b67be8fd27",
                19512, 1780656, 280, 706, 268, 160, 46, 288),
            ["level48_iso_world01"] = new(
                "91913d26c5afcec811f53de3765d181f131ec430860184b6c596a32bbac7a912",
                15156, 1434752, 225, 530, 217, 112, 46, 200),
        };

    public static void Run()
    {
        var fixture = CreateBank();
        var report = InspectBank(fixture);
        Expect(report is { Sounds: 1, Grains: 1, Samples: 1 }
            && report.OpcodeTypes is [1], "synthetic bank contract");
        var remap = InspectRemap(CreateAssetHeader(), report.Sounds);
        Expect(remap is { MobyConfigs: 1, OtherRecords: 1, GlobalIds: 1 },
            "synthetic sound-remap contract");

        var badChunk = fixture.ToArray();
        WriteUInt32(badChunk, 0x10, uint.MaxValue);
        ExpectThrows<InvalidDataException>(() => InspectBank(badChunk));
        var badGrainWindow = fixture.ToArray();
        WriteUInt32(badGrainWindow, 0x18 + 0x40 + 8, 8);
        ExpectThrows<InvalidDataException>(() => InspectBank(badGrainWindow));
        var unterminatedSample = fixture.ToArray();
        var sampleOffset = BinaryPrimitives.ReadInt32LittleEndian(fixture.AsSpan(0x10, 4));
        unterminatedSample[sampleOffset + 1] = 0;
        ExpectThrows<InvalidDataException>(() => InspectBank(unterminatedSample));

        var corpusRoot = Path.Combine("test-assets", "extractions_uya");
        var checkedBanks = 0;
        foreach (var pair in Corpus)
        {
            var bankPath = Path.Combine(corpusRoot, pair.Key, "level_wad", "sound.bnk");
            var headerPath = Path.Combine(corpusRoot, pair.Key, "assets", "asset_header.bin");
            if (!File.Exists(bankPath) || !File.Exists(headerPath)) continue;
            var bank = File.ReadAllBytes(bankPath);
            var actual = InspectBank(bank);
            var expected = pair.Value;
            Expect(Convert.ToHexStringLower(SHA256.HashData(bank)) == expected.Sha256,
                $"{pair.Key} complete-bank hash");
            Expect((actual.MetadataBytes, actual.SampleBytes, actual.Sounds, actual.Grains, actual.Samples)
                == (expected.MetadataBytes, expected.SampleBytes, expected.Sounds, expected.Grains, expected.Samples),
                $"{pair.Key} sound-bank structure");
            var actualRemap = InspectRemap(File.ReadAllBytes(headerPath), actual.Sounds);
            Expect((actualRemap.MobyConfigs, actualRemap.OtherRecords, actualRemap.GlobalIds)
                == (expected.MobyConfigs, expected.OtherRecords, expected.GlobalIds),
                $"{pair.Key} sound-remap structure");
            checkedBanks++;
        }
        if (checkedBanks > 0)
            Console.WriteLine($"UYA 989snd corpus: checked {checkedBanks} bank/remap pairs.");
    }

    private static BankReport InspectBank(byte[] bytes)
    {
        if (bytes.Length is < 24 or > MaximumBankBytes || bytes.Length % 2048 != 0)
            throw new InvalidDataException("UYA sound bank size or sector alignment is invalid.");
        var source = bytes.AsSpan();
        if (ReadUInt32(source, 0) != 3 || ReadUInt32(source, 4) != 2)
            throw new InvalidDataException("UYA sound bank requires a type-3 two-chunk container.");
        var metadataOffset = ToInt32(ReadUInt32(source, 8), "metadata offset");
        var metadataBytes = ToInt32(ReadUInt32(source, 12), "metadata size");
        var sampleOffset = ToInt32(ReadUInt32(source, 16), "sample offset");
        var sampleBytes = ToInt32(ReadUInt32(source, 20), "sample size");
        if (metadataOffset != 24 || metadataBytes > MaximumMetadataBytes
            || sampleBytes > MaximumSampleBytes || sampleBytes % 16 != 0
            || checked(metadataOffset + metadataBytes) != sampleOffset)
            throw new InvalidDataException("UYA sound bank chunk layout is invalid.");
        var metadata = Slice(source, metadataOffset, metadataBytes, "metadata");
        var samples = Slice(source, sampleOffset, sampleBytes, "samples");
        if (source[(sampleOffset + sampleBytes)..].ContainsAnyExcept((byte)0))
            throw new InvalidDataException("UYA sound bank sector padding is not zero.");

        if (metadata.Length < 0x40 || ReadUInt32(metadata, 0) != Sblk
            || ReadUInt32(metadata, 4) != 3 || ReadUInt32(metadata, 8) != 4
            || ReadUInt32(metadata, 0x0c) != 0x00574144)
            throw new InvalidDataException("UYA sound bank SBlk header is unsupported.");
        var soundCount = ReadNonNegativeInt16(metadata, 0x16, "sound count");
        var grainCount = ReadNonNegativeInt16(metadata, 0x18, "grain count");
        var sampleCount = ReadNonNegativeInt16(metadata, 0x1a, "sample count");
        var soundsOffset = ToInt32(ReadUInt32(metadata, 0x1c), "sound table offset");
        var grainsOffset = ToInt32(ReadUInt32(metadata, 0x20), "grain table offset");
        var grainDataOffset = ToInt32(ReadUInt32(metadata, 0x34), "grain data offset");
        if (ReadUInt32(metadata, 0x28) != sampleBytes || ReadUInt32(metadata, 0x2c) < sampleBytes
            || soundsOffset != 0x40
            || checked(soundsOffset + soundCount * 12) != grainsOffset
            || checked(grainsOffset + grainCount * 8) != grainDataOffset)
            throw new InvalidDataException("UYA sound bank table layout is invalid.");
        _ = Slice(metadata, soundsOffset, checked(soundCount * 12), "sound table");
        _ = Slice(metadata, grainsOffset, checked(grainCount * 8), "grain table");

        for (var index = 0; index < soundCount; index++)
        {
            var offset = soundsOffset + index * 12;
            var count = unchecked((sbyte)metadata[offset + 4]);
            var first = ToInt32(ReadUInt32(metadata, offset + 8), "first grain");
            if (count < 0 || first % 8 != 0 || first / 8 > grainCount - count)
                throw new InvalidDataException($"UYA sound {index} grain window is invalid.");
        }

        var types = new SortedSet<byte>();
        var sampleStarts = new HashSet<int>();
        for (var index = 0; index < grainCount; index++)
        {
            var offset = grainsOffset + index * 8;
            var opcode = ReadUInt32(metadata, offset);
            var type = (byte)(opcode >> 24);
            types.Add(type);
            var parameterSize = type switch
            {
                1 or 9 => 24,
                2 or 3 or 4 => 16,
                5 or 6 or 7 or 8 => 32,
                _ => 0,
            };
            if (parameterSize == 0) continue;
            var parameterOffset = checked(grainDataOffset + (int)(opcode & 0x00ffffff));
            var parameter = Slice(metadata, parameterOffset, parameterSize, $"grain {index} parameters");
            if (type is 1 or 9)
                sampleStarts.Add(ToInt32(ReadUInt32(parameter, 0x10), $"grain {index} sample offset"));
        }

        var ranges = new List<(int Start, int End)>();
        foreach (var start in sampleStarts.Order())
        {
            if (start < 0 || start % 16 != 0 || start >= samples.Length)
                throw new InvalidDataException("UYA sound sample offset is invalid.");
            var found = false;
            for (var offset = start; offset <= samples.Length - 16; offset += 16)
                if ((samples[offset + 1] & 1) != 0)
                {
                    ranges.Add((start, offset + 16));
                    found = true;
                    break;
                }
            if (!found) throw new InvalidDataException("UYA sound sample has no terminating ADPCM frame.");
        }
        for (var index = 1; index < ranges.Count; index++)
            if (ranges[index].Start < ranges[index - 1].End)
                throw new InvalidDataException("UYA sound sample ranges overlap.");
        if (ranges.Count != sampleCount)
            throw new InvalidDataException("UYA sound sample identity count does not match the block header.");
        return new(metadataBytes, sampleBytes, soundCount, grainCount, sampleCount, types.ToArray());
    }

    private static RemapReport InspectRemap(byte[] bytes, int soundCount)
    {
        var source = bytes.AsSpan();
        var mobyCount = ReadNonNegativeInt32(source, 0x18, "moby definition count");
        var remapOffset = ReadNonNegativeInt32(source, 0x70, "sound remap offset");
        var remap = source[remapOffset..];
        var otherOffset = ReadNonNegativeInt16(remap, 0, "other offset");
        var otherCount = ReadNonNegativeInt16(remap, 2, "other count");
        var globalOffset = ReadNonNegativeInt16(remap, 4, "global ID offset");
        var globalCount = ReadNonNegativeInt16(remap, 6, "global ID count");
        if (otherOffset < 8 || (otherOffset - 8) % 4 != 0)
            throw new InvalidDataException("UYA sound remap moby config table is invalid.");
        var configCount = (otherOffset - 8) / 4;
        if (configCount > mobyCount)
            throw new InvalidDataException("UYA sound remap has more moby configs than definitions.");
        _ = Slice(remap, otherOffset, checked(otherCount * 0x20), "other records");
        var global = Slice(remap, globalOffset, checked(globalCount * 4), "global IDs");
        ValidateSoundIds(global, soundCount, nullable: false);
        for (var index = 0; index < configCount; index++)
        {
            var offset = ReadNonNegativeInt16(remap, 8 + index * 4, $"moby config {index} offset");
            var count = ReadNonNegativeInt16(remap, 10 + index * 4, $"moby config {index} count");
            if (count == 0) continue;
            if (offset == 0) throw new InvalidDataException("UYA moby sound config is missing its list.");
            ValidateSoundIds(Slice(remap, offset, checked(count * 4), $"moby config {index} IDs"),
                soundCount, nullable: true);
        }
        return new(configCount, otherCount, globalCount);
    }

    private static void ValidateSoundIds(ReadOnlySpan<byte> bytes, int soundCount, bool nullable)
    {
        for (var offset = 0; offset < bytes.Length; offset += 4)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            if ((value < 0 && !(nullable && value == -1)) || value >= soundCount)
                throw new InvalidDataException("UYA sound remap contains an invalid sound index.");
        }
    }

    private static byte[] CreateBank()
    {
        const int metadataOffset = 0x18;
        const int metadataBytes = 0x6c;
        const int sampleOffset = metadataOffset + metadataBytes;
        var bytes = new byte[2048];
        WriteUInt32(bytes, 0x00, 3);
        WriteUInt32(bytes, 0x04, 2);
        WriteUInt32(bytes, 0x08, metadataOffset);
        WriteUInt32(bytes, 0x0c, metadataBytes);
        WriteUInt32(bytes, 0x10, sampleOffset);
        WriteUInt32(bytes, 0x14, 16);
        var block = bytes.AsSpan(metadataOffset, metadataBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(block, Sblk);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(block[8..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x0c..], 0x00574144);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x16..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x18..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x1a..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x1c..], 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x20..], 0x4c);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x28..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x2c..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x34..], 0x54);
        block[0x40] = 0x7f;
        block[0x44] = 1;
        block[0x45] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x4c..], 0x01000000);
        block[0x54] = 0x7f;
        block[0x55] = 0x7f;
        bytes[sampleOffset + 1] = 1;
        return bytes;
    }

    private static byte[] CreateAssetHeader()
    {
        var bytes = new byte[0x140];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x70), 0x100);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x100), 0x0c);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x102), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x104), 0x2c);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x106), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x108), 0x30);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x10a), 1);
        return bytes;
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> bytes, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"UYA sound {name} exceeds its container.");
        return bytes.Slice(offset, length);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, 4, "u32"));

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(Slice(bytes, offset, 4, name));
        return value >= 0 ? value : throw new InvalidDataException($"UYA sound {name} is negative.");
    }

    private static int ReadNonNegativeInt16(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(Slice(bytes, offset, 2, name));
        return value >= 0 ? value : throw new InvalidDataException($"UYA sound {name} is negative.");
    }

    private static int ToInt32(uint value, string name) => value <= int.MaxValue
        ? (int)value
        : throw new InvalidDataException($"UYA sound {name} exceeds signed bounds.");

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void Expect(bool condition, string context)
    {
        if (!condition) throw new InvalidOperationException(context);
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
