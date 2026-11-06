using System.Buffers.Binary;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaConvertedLevelPackageBuilder
{
    public static IReadOnlyList<PackedFile> Gameplay(IReadOnlyDictionary<string, byte[]> blocks, int alignment = 0x40)
    {
        var files = UyaGameplayLayout.Core.Blocks.Select(block => new PackedFile(
            $"gameplay/core/{block.SemanticName}.bin", blocks.GetValueOrDefault(block.SemanticName) ?? [], "application/octet-stream")).ToList();
        files.Add(new("gameplay/gameplay_core.bin", BuildBlocks(UyaGameplayLayout.CoreHeaderSize,
            UyaGameplayLayout.Core.Blocks.Select(block => (block.HeaderOffset, blocks.GetValueOrDefault(block.SemanticName) ?? [])), alignment: alignment), "application/octet-stream"));
        return files;
    }

    public static IReadOnlyList<PackedFile> LevelData(byte[] header, byte[] palette, byte[] assets,
        Func<string, byte[]> readFile, bool includeSourceCode = false)
    {
        var blocks = new List<(int, byte[])> {
            (0x08, header), (0x10, palette), (0x18, readFile("hud/header.bin")), (0x48, assets) };
        if (includeSourceCode) blocks.Add((0, readFile("code/code.bin")));
        for (var index = 0; index < 5; index++) blocks.Add((0x20 + index * 8, readFile($"hud/bank{index}.bin")));
        return UyaLevelWadUnpacker.UnpackLevelData(BuildBlocks(UyaLevelConstants.LevelDataHeaderSize, blocks.OrderBy(block => block.Item1), lengths: true));
    }

    private static byte[] BuildBlocks(int headerSize, IEnumerable<(int Offset, byte[] Bytes)> blocks,
        bool lengths = false, int alignment = 0x40)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(alignment));
        using var output = new MemoryStream(); output.Write(new byte[headerSize]);
        Span<byte> pointer = stackalloc byte[4];
        foreach (var (offset, bytes) in blocks)
        {
            if (bytes.Length == 0) continue;
            while (output.Length % alignment != 0) output.WriteByte(0);
            var position = checked((int)output.Length); output.Write(bytes);
            var saved = output.Position; output.Position = offset;
            BinaryPrimitives.WriteInt32LittleEndian(pointer, position); output.Write(pointer);
            if (lengths) { BinaryPrimitives.WriteInt32LittleEndian(pointer, bytes.Length); output.Write(pointer); }
            output.Position = saved;
        }
        return output.ToArray();
    }
}
