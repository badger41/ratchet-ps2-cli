using System.Buffers.Binary;
using RatchetPs2.Core.LevelAssets;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaSoundRemapComposer
{
    private const int HeaderSize = 8;
    private const int MobyConfigSize = 4;
    private const int OtherConfigSize = 0x20;

    public static byte[] Compose(
        ReadOnlySpan<byte> headerBytes,
        LevelAssetHeader header,
        IReadOnlyList<int> targetClassIds)
    {
        if (header.SoundRemapOffset == 0) return [];
        if (header.SoundRemapOffset < 0 || header.SoundRemapOffset >= headerBytes.Length)
            throw new InvalidDataException("UYA sound remap offset exceeds the asset header bounds.");

        var source = headerBytes[header.SoundRemapOffset..];
        var otherOffset = ReadNonNegativeInt16(source, 0, "other config offset");
        if (otherOffset < HeaderSize || (otherOffset - HeaderSize) % MobyConfigSize != 0)
            throw new InvalidDataException("UYA sound remap moby config table is invalid.");
        var sourceConfigCount = (otherOffset - HeaderSize) / MobyConfigSize;
        if (sourceConfigCount > header.MobyModelCount)
            throw new InvalidDataException("UYA sound remap has more moby configs than model definitions.");
        var sourceDefinitions = LevelAssetReader.ReadModelDefinitions(
            headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var sourceIndices = sourceDefinitions.Take(sourceConfigCount)
            .ToDictionary(value => value.ModelId, value => value.Index);
        var otherCount = ReadNonNegativeInt16(source, 2, "other config count");
        var soundIdOffset = ReadNonNegativeInt16(source, 4, "sound ID offset");
        var soundIdCount = ReadNonNegativeInt16(source, 6, "sound ID count");
        var otherBytes = Slice(source, otherOffset, checked(otherCount * OtherConfigSize), "other configs");
        var soundIdBytes = Slice(source, soundIdOffset, checked(soundIdCount * sizeof(int)), "sound IDs");

        using var output = new MemoryStream();
        output.Write(new byte[checked(HeaderSize + targetClassIds.Count * MobyConfigSize)]);
        var outputOtherOffset = ToInt16(output.Position, "other config offset");
        output.Write(otherBytes);
        var outputSoundIdOffset = ToInt16(output.Position, "sound ID offset");
        output.Write(soundIdBytes);

        for (var targetIndex = 0; targetIndex < targetClassIds.Count; targetIndex++)
        {
            if (!sourceIndices.TryGetValue(targetClassIds[targetIndex], out var sourceIndex)) continue;
            var configOffset = checked(HeaderSize + sourceIndex * MobyConfigSize);
            var start = BinaryPrimitives.ReadInt16LittleEndian(Slice(source, configOffset, 2, "moby sound config"));
            var count = BinaryPrimitives.ReadInt16LittleEndian(Slice(source, configOffset + 2, 2, "moby sound config"));
            if (count <= 0) continue;
            if (start <= 0) throw new InvalidDataException("UYA moby sound config has an invalid sound list offset.");
            var sounds = Slice(source, start, checked(count * sizeof(int)), "moby sound IDs");
            var outputStart = ToInt16(output.Position, "moby sound list offset");
            output.Write(sounds);
            WriteInt16(output.GetBuffer(), HeaderSize + targetIndex * MobyConfigSize, outputStart);
            WriteInt16(output.GetBuffer(), HeaderSize + targetIndex * MobyConfigSize + 2, count);
        }

        var bytes = output.ToArray();
        WriteInt16(bytes, 0, outputOtherOffset);
        WriteInt16(bytes, 2, checked((short)otherCount));
        WriteInt16(bytes, 4, outputSoundIdOffset);
        WriteInt16(bytes, 6, checked((short)soundIdCount));
        return bytes;
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> bytes, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"UYA sound remap {name} exceed the asset header bounds.");
        return bytes.Slice(offset, length);
    }

    private static int ReadNonNegativeInt16(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(Slice(bytes, offset, 2, name));
        if (value < 0) throw new InvalidDataException($"UYA sound remap {name} is invalid.");
        return value;
    }

    private static short ToInt16(long value, string name)
    {
        if (value > short.MaxValue) throw new InvalidDataException($"UYA sound remap {name} exceeds 16-bit bounds.");
        return (short)value;
    }

    private static void WriteInt16(byte[] bytes, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, sizeof(short)), value);
}
