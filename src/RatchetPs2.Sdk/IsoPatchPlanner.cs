using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class IsoPatchPlanner
{
    public const int SchemaVersion = 1;

    public static IsoPatchPlan Create(
        GameId gameId,
        Stream iso,
        int levelIndex,
        ReadOnlySpan<byte> outputLevelWad,
        bool forceFullImage = false,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaIsoPatchPlanner.Create(
                iso, levelIndex, outputLevelWad, forceFullImage, cancellationToken),
            _ => throw new NotSupportedException($"ISO patch planning is not supported for {gameId}."),
        };

    public static IsoPatchPlan Create(
        GameId gameId,
        Stream iso,
        IsoLevelAllocation allocation,
        ReadOnlySpan<byte> outputLevelWad,
        bool forceFullImage = false,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaIsoPatchPlanner.Create(
                iso, allocation, outputLevelWad, forceFullImage, cancellationToken),
            _ => throw new NotSupportedException($"ISO patch planning is not supported for {gameId}."),
        };
}
