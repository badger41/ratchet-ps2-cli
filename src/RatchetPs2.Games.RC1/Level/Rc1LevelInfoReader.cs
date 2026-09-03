using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelInfoReader
{
    public static Rc1LevelInfoEntry ReadLevel(Stream isoStream, int levelId)
    {
        ValidateIsoStream(isoStream);

        if (levelId < 0 || levelId >= Rc1LevelConstants.LevelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(levelId),
                $"RC1 level id must be between 0 and {Rc1LevelConstants.LevelCount - 1}.");
        }

        var tocOffset = (long)Rc1LevelConstants.TableOfContentsSector * Rc1LevelConstants.SectorSize;
        if (tocOffset + Rc1LevelConstants.TableOfContentsSize > isoStream.Length)
        {
            throw new InvalidDataException("RC1 table of contents exceeds the ISO stream length.");
        }

        isoStream.Position = tocOffset;
        var magic = isoStream.ReadInt32LittleEndian();
        var tocSize = isoStream.ReadInt32LittleEndian();
        if (magic != 1 || tocSize != Rc1LevelConstants.TableOfContentsSize)
        {
            throw new InvalidDataException(
                $"Invalid RC1 table of contents header (magic 0x{magic:X}, size 0x{tocSize:X}).");
        }

        isoStream.Position = tocOffset + Rc1LevelConstants.LevelTableOffset;
        var tableBytes = isoStream.ReadBytesExactly(Rc1LevelConstants.LevelCount * 8);
        for (var tableIndex = 0; tableIndex < Rc1LevelConstants.LevelCount; tableIndex++)
        {
            var tableRange = ReadSectorRange(tableBytes, tableIndex * 8);
            if (tableRange.IsEmpty || tableRange.Offset.Value <= 0)
            {
                continue;
            }

            var header = ReadAmalgamatedHeader(isoStream, tableRange.Offset.Value);
            if (header.Level == levelId)
            {
                return new Rc1LevelInfoEntry(tableIndex, tableRange, header);
            }
        }

        throw new InvalidDataException($"RC1 level {levelId} was not found in the ISO table of contents.");
    }

    public static Rc1AmalgamatedLevelHeader ReadAmalgamatedHeader(Stream isoStream, int headerSector)
    {
        var bytes = ReadAbsoluteSectorBytes(
            isoStream,
            headerSector,
            Rc1LevelConstants.AmalgamatedHeaderSize);
        var headerSize = BinarySpanReader.ReadInt32LittleEndian(bytes, 0x04);
        if (headerSize != Rc1LevelConstants.AmalgamatedHeaderSize)
        {
            throw new InvalidDataException(
                $"RC1 level header at sector 0x{headerSector:X} has invalid size 0x{headerSize:X}.");
        }

        var audioData = new Rc1SectorByteRange[36];
        for (var i = 0; i < audioData.Length; i++)
        {
            audioData[i] = ReadSectorByteRange(bytes, 0x28 + (i * 8));
        }

        var music = new Sector32[15];
        for (var i = 0; i < music.Length; i++)
        {
            music[i] = new Sector32(BinarySpanReader.ReadInt32LittleEndian(bytes, 0x148 + (i * 4)));
        }

        var scenes = new Rc1SceneHeader[30];
        for (var sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++)
        {
            var sceneOffset = 0x184 + (sceneIndex * 0x128);
            var sounds = new Sector32[6];
            var wads = new Sector32[68];
            for (var i = 0; i < sounds.Length; i++)
            {
                sounds[i] = new Sector32(BinarySpanReader.ReadInt32LittleEndian(bytes, sceneOffset + (i * 4)));
            }

            for (var i = 0; i < wads.Length; i++)
            {
                wads[i] = new Sector32(BinarySpanReader.ReadInt32LittleEndian(bytes, sceneOffset + 0x18 + (i * 4)));
            }

            scenes[sceneIndex] = new Rc1SceneHeader(sounds, wads);
        }

        return new Rc1AmalgamatedLevelHeader(
            BinarySpanReader.ReadInt32LittleEndian(bytes, 0x00),
            ReadSectorRange(bytes, 0x08),
            ReadSectorRange(bytes, 0x10),
            ReadSectorRange(bytes, 0x18),
            ReadSectorRange(bytes, 0x20),
            audioData,
            music,
            scenes);
    }

    public static byte[] ReadAbsoluteSectorRange(Stream isoStream, int absoluteSector, int sectorCount)
    {
        if (sectorCount < 0)
        {
            throw new InvalidDataException("RC1 sector count cannot be negative.");
        }

        var byteLength = (long)sectorCount * Rc1LevelConstants.SectorSize;
        if (byteLength > int.MaxValue)
        {
            throw new InvalidDataException("RC1 ISO range is too large to materialize.");
        }

        return ReadAbsoluteSectorBytes(
            isoStream,
            absoluteSector,
            (int)byteLength);
    }

    public static byte[] ReadAbsoluteSectorBytes(Stream isoStream, int absoluteSector, int byteLength)
    {
        ValidateIsoStream(isoStream);
        if (absoluteSector < 0 || byteLength < 0)
        {
            throw new InvalidDataException("RC1 ISO ranges cannot contain negative values.");
        }

        var offset = checked((long)absoluteSector * Rc1LevelConstants.SectorSize);
        if (offset + byteLength > isoStream.Length)
        {
            throw new InvalidDataException(
                $"RC1 ISO range at sector 0x{absoluteSector:X} length 0x{byteLength:X} exceeds the stream length.");
        }

        isoStream.Position = offset;
        return isoStream.ReadBytesExactly(byteLength);
    }

    private static SectorRange ReadSectorRange(ReadOnlySpan<byte> bytes, int offset)
    {
        return new SectorRange(
            new Sector32(BinarySpanReader.ReadInt32LittleEndian(bytes, offset)),
            new Sector32(BinarySpanReader.ReadInt32LittleEndian(bytes, offset + 4)));
    }

    private static Rc1SectorByteRange ReadSectorByteRange(ReadOnlySpan<byte> bytes, int offset)
    {
        return new Rc1SectorByteRange(
            BinarySpanReader.ReadInt32LittleEndian(bytes, offset),
            BinarySpanReader.ReadInt32LittleEndian(bytes, offset + 4));
    }

    private static void ValidateIsoStream(Stream isoStream)
    {
        ArgumentNullException.ThrowIfNull(isoStream);
        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The provided ISO stream must be readable and seekable.", nameof(isoStream));
        }
    }
}
