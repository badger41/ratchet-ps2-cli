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

    /// <summary>Serializes explicit nonempty coverage and masks without a source grid or inferred reserved-bit defaults.</summary>
    public static byte[] CreateGrid(GameId gameId,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionGridWriter.Create(masks, token),
            _ => throw new NotSupportedException($"Occlusion grid creation does not support {gameId}."),
        };

    /// <summary>Creates a native empty template with no octants or visibility masks.</summary>
    public static byte[] CreateEmptyGrid(GameId gameId, CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionGridWriter.CreateEmpty(token),
        _ => throw new NotSupportedException($"Empty occlusion grids do not support {gameId}."),
    };

    /// <summary>Replaces coverage with an empty native template, leaving the rest of the level intact.</summary>
    public static LevelArchiveBuildResult ClearCoverageFromLevel(GameId gameId, ReadOnlyMemory<byte> levelWad,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionComposer.ClearCoverageFromLevel(levelWad, token),
        _ => throw new NotSupportedException($"Clearing occlusion coverage does not support {gameId}."),
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

    /// <summary>Applies explicit nonempty coverage and complete masks, allowing new coordinates and allocation growth.</summary>
    /// <remarks>The caller supplies every mask bit; this API does not qualify runtime budgets or infer new-cell defaults.</remarks>
    public static LevelArchiveBuildResult ApplyCoverageToLevel(GameId gameId, ReadOnlyMemory<byte> levelWad,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default) => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.ApplyCoverageToLevel(levelWad, masks, token),
            _ => throw new NotSupportedException($"Occlusion coverage composition does not support {gameId}."),
        };

    /// <summary>Applies a validated generated grid without source-coordinate or source-allocation restrictions.</summary>
    public static LevelArchiveBuildResult ApplyCoverageToLevel(GameId gameId, ReadOnlyMemory<byte> levelWad,
        byte[] generated, int octants, CancellationToken token = default) => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.ApplyCoverageToLevel(levelWad, generated, octants, token),
            _ => throw new NotSupportedException($"Occlusion coverage composition does not support {gameId}."),
        };

    public static LevelArchiveBuildResult ApplyToLevel(GameId gameId, ReadOnlyMemory<byte> levelWad,
        byte[] generated, int octants, CancellationToken token = default) => gameId switch
        {
            GameId.UYA => UyaOcclusionComposer.ApplyToLevel(levelWad, generated, octants, token),
            _ => throw new NotSupportedException($"Occlusion archive composition does not support {gameId}."),
        };
}
