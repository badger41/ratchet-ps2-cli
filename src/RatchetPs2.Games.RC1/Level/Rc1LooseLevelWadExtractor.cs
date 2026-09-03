using System.Buffers.Binary;
using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LooseLevelWadExtractor
{
    public static Rc1LooseLevelWad ExtractPrimary(Stream isoStream, int levelId)
    {
        return ExtractPrimary(isoStream, Rc1LevelInfoReader.ReadLevel(isoStream, levelId));
    }

    public static Rc1ExtractedLevelWads ExtractAll(Stream isoStream, int levelId)
    {
        var levelInfo = Rc1LevelInfoReader.ReadLevel(isoStream, levelId);
        return new Rc1ExtractedLevelWads(
            ExtractPrimary(isoStream, levelInfo),
            ExtractAudio(isoStream, levelInfo),
            ExtractScene(isoStream, levelInfo));
    }

    public static Rc1LooseLevelWad ExtractPrimary(Stream isoStream, Rc1LevelInfoEntry levelInfo)
    {
        ArgumentNullException.ThrowIfNull(isoStream);
        ArgumentNullException.ThrowIfNull(levelInfo);

        var header = levelInfo.Header;
        var ranges = new[] { header.Data, header.GameplayNtsc, header.GameplayPal, header.Occlusion };
        var (payloadBaseSector, sectorCount) = GetOutputRange(
            Rc1LevelConstants.LevelWadHeaderSize,
            ranges.Where(range => !range.IsEmpty).Select(range => (range.Offset.Value, range.Size.Value)));
        var bytes = CreateBuffer(sectorCount);

        WriteInt32(bytes, 0x00, Rc1LevelConstants.LevelWadHeaderSize);
        WriteInt32(bytes, 0x08, header.Level);
        WriteSectorRange(bytes, 0x10, MakeRelative(header.Data, payloadBaseSector));
        WriteSectorRange(bytes, 0x18, MakeRelative(header.GameplayNtsc, payloadBaseSector));
        WriteSectorRange(bytes, 0x20, MakeRelative(header.GameplayPal, payloadBaseSector));
        WriteSectorRange(bytes, 0x28, MakeRelative(header.Occlusion, payloadBaseSector));

        foreach (var range in ranges)
        {
            CopySectorRange(isoStream, range.Offset.Value, range.Size.Value, payloadBaseSector, bytes);
        }

        var levelWad = Rc1LevelWadReader.ReadLevelWad(bytes);
        return new Rc1LooseLevelWad(
            header.Level,
            levelInfo.TableRange.Offset.Value,
            payloadBaseSector,
            sectorCount,
            levelInfo,
            levelWad,
            bytes);
    }

    public static byte[] ExtractAudio(Stream isoStream, Rc1LevelInfoEntry levelInfo)
    {
        ArgumentNullException.ThrowIfNull(isoStream);
        ArgumentNullException.ThrowIfNull(levelInfo);

        var musicSizes = new Dictionary<int, int>();
        foreach (var music in levelInfo.Header.Music)
        {
            if (music.Value > 0 && !musicSizes.ContainsKey(music.Value))
            {
                musicSizes[music.Value] = GetVagSectorCount(isoStream, music.Value);
            }
        }

        var ranges = levelInfo.Header.AudioData
            .Where(range => !range.IsEmpty && range.Offset > 0)
            .Select(range => (range.Offset, AlignToSectorCount(range.Length)))
            .Concat(musicSizes.Select(pair => (pair.Key, pair.Value)))
            .ToArray();
        if (ranges.Length == 0)
        {
            return [];
        }

        var (payloadBaseSector, sectorCount) = GetOutputRange(Rc1LevelConstants.LevelAudioWadHeaderSize, ranges);
        var bytes = CreateBuffer(sectorCount);
        WriteInt32(bytes, 0x00, Rc1LevelConstants.LevelAudioWadHeaderSize);

        for (var i = 0; i < levelInfo.Header.AudioData.Count; i++)
        {
            var range = levelInfo.Header.AudioData[i];
            if (range.IsEmpty || range.Offset <= 0)
            {
                continue;
            }

            WriteInt32(bytes, 0x08 + (i * 8), checked(range.Offset - payloadBaseSector));
            WriteInt32(bytes, 0x0c + (i * 8), range.Length);
            CopyByteRange(isoStream, range.Offset, range.Length, payloadBaseSector, bytes);
        }

        for (var i = 0; i < levelInfo.Header.Music.Count; i++)
        {
            var sourceSector = levelInfo.Header.Music[i].Value;
            if (sourceSector <= 0)
            {
                continue;
            }

            WriteInt32(bytes, 0x128 + (i * 4), checked(sourceSector - payloadBaseSector));
            CopySectorRange(isoStream, sourceSector, musicSizes[sourceSector], payloadBaseSector, bytes);
        }

        return bytes;
    }

    public static byte[] ExtractScene(Stream isoStream, Rc1LevelInfoEntry levelInfo)
    {
        ArgumentNullException.ThrowIfNull(isoStream);
        ArgumentNullException.ThrowIfNull(levelInfo);

        var soundSizes = new Dictionary<int, int>();
        var wadSizes = new Dictionary<int, int>();
        foreach (var scene in levelInfo.Header.Scenes)
        {
            foreach (var sound in scene.Sounds)
            {
                if (sound.Value > 0 && !soundSizes.ContainsKey(sound.Value))
                {
                    soundSizes[sound.Value] = GetVagSectorCount(isoStream, sound.Value);
                }
            }

            foreach (var wad in scene.Wads)
            {
                if (wad.Value > 0 && !wadSizes.ContainsKey(wad.Value))
                {
                    wadSizes[wad.Value] = GetCompressedWadSectorCount(isoStream, wad.Value);
                }
            }
        }

        var ranges = soundSizes.Select(pair => (pair.Key, pair.Value))
            .Concat(wadSizes.Select(pair => (pair.Key, pair.Value)))
            .ToArray();
        if (ranges.Length == 0)
        {
            return [];
        }

        var (payloadBaseSector, sectorCount) = GetOutputRange(Rc1LevelConstants.LevelSceneWadHeaderSize, ranges);
        var bytes = CreateBuffer(sectorCount);
        WriteInt32(bytes, 0x00, Rc1LevelConstants.LevelSceneWadHeaderSize);

        for (var sceneIndex = 0; sceneIndex < levelInfo.Header.Scenes.Count; sceneIndex++)
        {
            var scene = levelInfo.Header.Scenes[sceneIndex];
            var sceneOffset = 0x08 + (sceneIndex * 0x128);
            for (var i = 0; i < scene.Sounds.Count; i++)
            {
                var sourceSector = scene.Sounds[i].Value;
                if (sourceSector <= 0)
                {
                    continue;
                }

                WriteInt32(bytes, sceneOffset + (i * 4), checked(sourceSector - payloadBaseSector));
                CopySectorRange(isoStream, sourceSector, soundSizes[sourceSector], payloadBaseSector, bytes);
            }

            for (var i = 0; i < scene.Wads.Count; i++)
            {
                var sourceSector = scene.Wads[i].Value;
                if (sourceSector <= 0)
                {
                    continue;
                }

                WriteInt32(bytes, sceneOffset + 0x18 + (i * 4), checked(sourceSector - payloadBaseSector));
                CopySectorRange(isoStream, sourceSector, wadSizes[sourceSector], payloadBaseSector, bytes);
            }
        }

        return bytes;
    }

    private static (int PayloadBaseSector, int SectorCount) GetOutputRange(
        int headerSize,
        IEnumerable<(int Offset, int Length)> ranges)
    {
        var populated = ranges.Where(range => range.Offset > 0 && range.Length > 0).ToArray();
        if (populated.Length == 0)
        {
            throw new InvalidDataException("RC1 WAD has no populated data ranges.");
        }

        var headerSectors = AlignToSectorCount(headerSize);
        var low = populated.Min(range => range.Offset);
        var high = populated.Max(range => checked(range.Offset + range.Length));
        var payloadBaseSector = checked(low - headerSectors);
        if (payloadBaseSector < 0 || high <= payloadBaseSector)
        {
            throw new InvalidDataException("RC1 WAD sector range is invalid.");
        }

        return (payloadBaseSector, checked(high - payloadBaseSector));
    }

    private static SectorRange MakeRelative(SectorRange range, int payloadBaseSector)
    {
        return range.IsEmpty
            ? new SectorRange(new Sector32(0), new Sector32(0))
            : new SectorRange(
                new Sector32(checked(range.Offset.Value - payloadBaseSector)),
                range.Size);
    }

    private static void CopySectorRange(
        Stream isoStream,
        int sourceSector,
        int sectorCount,
        int payloadBaseSector,
        byte[] destination)
    {
        if (sectorCount <= 0)
        {
            return;
        }

        var bytes = Rc1LevelInfoReader.ReadAbsoluteSectorRange(isoStream, sourceSector, sectorCount);
        var destinationOffset = checked((sourceSector - payloadBaseSector) * Rc1LevelConstants.SectorSize);
        bytes.CopyTo(destination.AsSpan(destinationOffset));
    }

    private static void CopyByteRange(
        Stream isoStream,
        int sourceSector,
        int byteLength,
        int payloadBaseSector,
        byte[] destination)
    {
        if (byteLength <= 0)
        {
            return;
        }

        var bytes = Rc1LevelInfoReader.ReadAbsoluteSectorBytes(isoStream, sourceSector, byteLength);
        var destinationOffset = checked((sourceSector - payloadBaseSector) * Rc1LevelConstants.SectorSize);
        bytes.CopyTo(destination.AsSpan(destinationOffset));
    }

    private static int GetVagSectorCount(Stream isoStream, int sector)
    {
        var header = Rc1LevelInfoReader.ReadAbsoluteSectorRange(isoStream, sector, 1);
        if (!header.AsSpan(0, 4).SequenceEqual("VAGp"u8))
        {
            return 1;
        }

        var dataSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0x0c, 4));
        return dataSize < 0 ? 1 : AlignToSectorCount(checked(dataSize + 0x30));
    }

    private static int GetCompressedWadSectorCount(Stream isoStream, int sector)
    {
        var header = Rc1LevelInfoReader.ReadAbsoluteSectorRange(isoStream, sector, 1);
        if (!header.AsSpan(0, 3).SequenceEqual("WAD"u8))
        {
            return 1;
        }

        var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(3, 4));
        return compressedSize <= 0 ? 1 : AlignToSectorCount(compressedSize);
    }

    private static int AlignToSectorCount(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new InvalidDataException("RC1 byte length cannot be negative.");
        }

        return checked((byteLength + Rc1LevelConstants.SectorSize - 1) / Rc1LevelConstants.SectorSize);
    }

    private static byte[] CreateBuffer(int sectorCount)
    {
        var byteLength = checked((long)sectorCount * Rc1LevelConstants.SectorSize);
        if (byteLength > int.MaxValue)
        {
            throw new InvalidDataException("RC1 WAD is too large to materialize.");
        }

        return new byte[(int)byteLength];
    }

    private static void WriteSectorRange(byte[] destination, int offset, SectorRange range)
    {
        WriteInt32(destination, offset, range.Offset.Value);
        WriteInt32(destination, offset + 4, range.Size.Value);
    }

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, 4), value);
    }
}
