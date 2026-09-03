using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelWadReader
{
    public static Rc1LevelWad ReadLevelWad(ReadOnlySpan<byte> data)
    {
        if (data.Length < Rc1LevelConstants.LevelWadHeaderSize)
        {
            throw new InvalidDataException("RC1 level WAD is shorter than its header.");
        }

        var headerSize = BinarySpanReader.ReadInt32LittleEndian(data, 0x00);
        if (headerSize != Rc1LevelConstants.LevelWadHeaderSize)
        {
            throw new InvalidDataException($"Invalid RC1 level WAD header size 0x{headerSize:X}.");
        }

        return new Rc1LevelWad(
            headerSize,
            BinarySpanReader.ReadInt32LittleEndian(data, 0x08),
            ReadSectorRange(data, 0x10),
            ReadSectorRange(data, 0x18),
            ReadSectorRange(data, 0x20),
            ReadSectorRange(data, 0x28),
            data[..headerSize].ToArray());
    }

    public static Rc1LevelDataWad ReadLevelDataWad(ReadOnlySpan<byte> data)
    {
        if (data.Length < Rc1LevelConstants.LevelDataHeaderSize)
        {
            throw new InvalidDataException("RC1 level data WAD is shorter than its header.");
        }

        var hudBanks = new Rc1ByteRange[5];
        for (var i = 0; i < hudBanks.Length; i++)
        {
            hudBanks[i] = ReadByteRange(data, 0x28 + (i * 8));
        }

        return new Rc1LevelDataWad(
            ReadByteRange(data, 0x00),
            ReadByteRange(data, 0x08),
            ReadByteRange(data, 0x10),
            ReadByteRange(data, 0x18),
            ReadByteRange(data, 0x20),
            hudBanks,
            ReadByteRange(data, 0x50),
            data[..Rc1LevelConstants.LevelDataHeaderSize].ToArray());
    }

    public static byte[] ReadSectorFileBlock(ReadOnlySpan<byte> container, SectorRange block)
    {
        if (block.IsEmpty)
        {
            return [];
        }

        if (block.Offset.Value < 0 || block.Size.Value < 0)
        {
            throw new InvalidDataException("RC1 sector range cannot contain negative values.");
        }

        return ReadBlock(
            container,
            checked((long)block.Offset.Value * Rc1LevelConstants.SectorSize),
            checked((long)block.Size.Value * Rc1LevelConstants.SectorSize),
            "sector range");
    }

    public static byte[] ReadByteFileBlock(ReadOnlySpan<byte> container, Rc1ByteRange block)
    {
        if (block.IsEmpty)
        {
            return [];
        }

        if (block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException("RC1 byte range cannot contain negative values.");
        }

        return ReadBlock(container, block.Offset, block.Length, "byte range");
    }

    private static byte[] ReadBlock(ReadOnlySpan<byte> container, long offset, long length, string description)
    {
        if (offset < 0 || length < 0 || offset > container.Length || length > container.Length - offset)
        {
            throw new InvalidDataException(
                $"RC1 {description} 0x{offset:X}+0x{length:X} exceeds container length 0x{container.Length:X}.");
        }

        if (length > int.MaxValue)
        {
            throw new InvalidDataException($"RC1 {description} is too large to materialize.");
        }

        return container.Slice((int)offset, (int)length).ToArray();
    }

    private static SectorRange ReadSectorRange(ReadOnlySpan<byte> data, int offset)
    {
        return new SectorRange(
            new Sector32(BinarySpanReader.ReadInt32LittleEndian(data, offset)),
            new Sector32(BinarySpanReader.ReadInt32LittleEndian(data, offset + 4)));
    }

    private static Rc1ByteRange ReadByteRange(ReadOnlySpan<byte> data, int offset)
    {
        return new Rc1ByteRange(
            BinarySpanReader.ReadInt32LittleEndian(data, offset),
            BinarySpanReader.ReadInt32LittleEndian(data, offset + 4));
    }
}
