using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class IsoReplacementBuilder
{
    public static Task BuildAsync(
        GameId gameId,
        Stream source,
        Stream destination,
        IsoPatchPlan plan,
        Func<long, long, ValueTask>? progress = null,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaIsoReplacementBuilder.BuildAsync(
                source, destination, plan, progress, cancellationToken),
            _ => throw new NotSupportedException($"ISO replacement builds are not supported for {gameId}."),
        };

    public static void Verify(GameId gameId, Stream iso, IsoPatchPlan plan)
    {
        if (gameId == GameId.UYA) UyaIsoReplacementBuilder.Verify(iso, plan);
        else throw new NotSupportedException($"ISO replacement verification is not supported for {gameId}.");
    }
}
