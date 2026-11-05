using System.Buffers.Binary;

namespace RatchetPs2.Games.RC1.Level;

internal static class Rc1LevelArchiveReader
{
    public static IReadOnlyList<int> FindAvailable(Stream iso)
    {
        var toc = Rc1LevelInfoReader.ReadAbsoluteSectorBytes(iso,
            Rc1LevelConstants.TableOfContentsSector, Rc1LevelConstants.TableOfContentsSize);
        if (BinaryPrimitives.ReadInt32LittleEndian(toc) != 1
            || BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(4)) != Rc1LevelConstants.TableOfContentsSize)
            throw new InvalidDataException("RC1 table of contents is invalid.");
        var levels = new SortedSet<int>();
        for (var index = 0; index < Rc1LevelConstants.LevelCount; index++)
        {
            var offset = Rc1LevelConstants.LevelTableOffset + index * 8;
            var sector = BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(offset));
            var count = BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(offset + 4));
            if (sector < 0 || count < 0) throw new InvalidDataException("RC1 level table contains a negative range.");
            if (sector == 0 || count == 0) continue;
            var header = Rc1LevelInfoReader.ReadAmalgamatedHeader(iso, sector);
            if (header.Level < 0 || header.Level >= Rc1LevelConstants.LevelCount || !levels.Add(header.Level))
                throw new InvalidDataException("RC1 level table contains an invalid or duplicate level ID.");
        }
        return levels.ToArray();
    }
}
