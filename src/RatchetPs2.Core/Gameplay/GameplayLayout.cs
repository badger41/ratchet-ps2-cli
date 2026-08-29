using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Gameplay;

public sealed record GameplayLayout(
    string GameName,
    string Kind,
    int HeaderSize,
    IReadOnlyList<GameplayBlockDefinition> Blocks);

public readonly record struct GameplayBlockDefinition(
    int HeaderOffset,
    string SemanticName);

public sealed record GameplayRawBlocks(
    GameplayLayout Layout,
    byte[] HeaderBytes,
    IReadOnlyList<GameplayRawBlock> Blocks);

public sealed record GameplayRawBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes);

public static class GameplayLayoutReader
{
    public static GameplayRawBlocks Read(ReadOnlySpan<byte> data, GameplayLayout layout)
    {
        if (data.Length < layout.HeaderSize)
        {
            throw new InvalidDataException(
                $"{layout.GameName} {layout.Kind} gameplay data is too small to contain the 0x{layout.HeaderSize:X}-byte pointer table.");
        }

        var pointers = new GameplayPointer[layout.Blocks.Count];
        var sortedPointerList = new List<int>(layout.Blocks.Count);
        for (var index = 0; index < layout.Blocks.Count; index++)
        {
            var block = layout.Blocks[index];
            var pointer = ReadInt32LittleEndian(data, block.HeaderOffset);
            pointers[index] = new GameplayPointer(index, block, pointer);
            if (pointer > 0 && pointer <= data.Length)
            {
                sortedPointerList.Add(pointer);
            }
        }
        var sortedPointers = sortedPointerList.OrderBy(pointer => pointer).ToArray();
        var blocks = new List<GameplayRawBlock>(pointers.Length);

        foreach (var pointer in pointers)
        {
            if (pointer.Pointer < 0 || pointer.Pointer > data.Length)
            {
                throw new InvalidDataException(
                    $"{layout.GameName} {layout.Kind} gameplay slot 0x{pointer.Block.HeaderOffset:X2} points outside gameplay bounds.");
            }

            byte[] payload = [];
            if (pointer.Pointer > 0)
            {
                var nextPointer = sortedPointers.FirstOrDefault(candidate => candidate > pointer.Pointer);
                if (nextPointer == 0)
                {
                    nextPointer = data.Length;
                }

                payload = SliceToArray(
                    data,
                    pointer.Pointer,
                    nextPointer - pointer.Pointer,
                    $"{layout.GameName} {layout.Kind} gameplay slot 0x{pointer.Block.HeaderOffset:X2}");
            }

            blocks.Add(new GameplayRawBlock(
                pointer.Index,
                pointer.Block.HeaderOffset,
                pointer.Pointer,
                pointer.Block.SemanticName,
                payload));
        }

        return new GameplayRawBlocks(
            layout,
            data[..layout.HeaderSize].ToArray(),
            blocks);
    }

    private readonly record struct GameplayPointer(
        int Index,
        GameplayBlockDefinition Block,
        int Pointer);
}
