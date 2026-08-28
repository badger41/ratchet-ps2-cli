using System.Buffers.Binary;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Games.DL.Level;

public static class DlMissionDataReader
{
    public static byte[] ReadGameplay(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x40)
        {
            return [];
        }

        var gameplayLength = BinaryPrimitives.ReadInt32LittleEndian(data[0x04..]);
        if (gameplayLength <= 0 || (long)0x40 + gameplayLength > data.Length)
        {
            return [];
        }

        var gameplay = data.Slice(0x40, gameplayLength).ToArray();
        if (!BinaryMagic.IsWad(gameplay))
        {
            return gameplay;
        }

        try
        {
            return WadCompression.Decompress(gameplay);
        }
        catch (InvalidDataException)
        {
            return gameplay;
        }
    }

    public static byte[] ReadClasses(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10)
        {
            return [];
        }

        var gameplayOffset = BinaryPrimitives.ReadInt32LittleEndian(data[0x00..]);
        var classesOffset = BinaryPrimitives.ReadInt32LittleEndian(data[0x08..]);
        var classesLength = BinaryPrimitives.ReadInt32LittleEndian(data[0x0c..]);
        var localClassesOffset = classesOffset - (gameplayOffset - 0x40);
        if (localClassesOffset < 0
            || classesLength <= 0
            || (long)localClassesOffset + classesLength > data.Length)
        {
            return [];
        }

        var classes = data.Slice(localClassesOffset, classesLength).ToArray();
        if (!BinaryMagic.IsWad(classes))
        {
            return classes;
        }

        try
        {
            return WadCompression.Decompress(classes);
        }
        catch (InvalidDataException)
        {
            return classes;
        }
    }

    public static bool IsPlaceholderMissionData(ReadOnlySpan<byte> data)
    {
        if (data.Length != DlLevelConstants.SectorSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(data[0x00..]) != -1
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x04..]) != 0
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x08..]) != -1
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x0c..]) != 0)
        {
            return false;
        }

        for (var i = 0x10; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
