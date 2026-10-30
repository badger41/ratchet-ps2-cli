using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Occlusion;

namespace RatchetPs2.Sdk;

public static class OcclusionComposer
{
    public static byte[] ReadGrid(GameId gameId, ReadOnlySpan<byte> levelWad, CancellationToken token = default)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.ReadGrid(levelWad, token),
            _ => throw new NotSupportedException($"Occlusion grid extraction does not support {gameId}."),
        };

    public static byte[] ReplaceMasks(GameId gameId, ReadOnlySpan<byte> grid,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionGridWriter.ReplaceMasks(grid, masks, token),
            _ => throw new NotSupportedException($"Occlusion mask replacement does not support {gameId}."),
        };

    /// <summary>Unions similar masks as needed to fit the source allocation; never removes generated visibility.</summary>
    public static byte[] RebuildGrid(GameId gameId, ReadOnlySpan<byte> original,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.RebuildGrid(original, masks, token),
            _ => throw new NotSupportedException($"Occlusion grid rebuilding does not support {gameId}."),
        };

    /// <summary>Compacts an existing generated grid against its source allocation, preserving coordinates and visibility.</summary>
    public static byte[] CompactGrid(GameId gameId, ReadOnlySpan<byte> original, byte[] generated, CancellationToken token = default)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.CompactGrid(original, generated, token),
            _ => throw new NotSupportedException($"Occlusion grid compaction does not support {gameId}."),
        };

    public static void ValidateGrid(GameId gameId, ReadOnlySpan<byte> original, ReadOnlySpan<byte> generated,
        int octants, CancellationToken token = default)
    {
        switch (gameId)
        {
            case GameId.UYA: UyaOcclusionComposer.ValidateGrid(original, generated, octants, token); break;
            default: throw new NotSupportedException($"Occlusion grid validation does not support {gameId}.");
        }
    }

    public static LevelArchiveBuildResult ApplyToLevel(GameId gameId, ReadOnlyMemory<byte> levelWad,
        byte[] generated, int octants, CancellationToken token = default) => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.ApplyToLevel(levelWad, generated, octants, token),
            _ => throw new NotSupportedException($"Occlusion archive composition does not support {gameId}."),
        };
}
