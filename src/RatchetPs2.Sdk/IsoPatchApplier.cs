using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class IsoPatchApplier
{
    public static void ValidateSource(
        GameId gameId, Stream iso, IsoPatchPlan plan, CancellationToken cancellationToken = default)
    {
        if (gameId == GameId.UYA) UyaIsoPatchApplier.ValidateSource(iso, plan, cancellationToken);
        else throw Unsupported(gameId);
    }

    public static void ApplyRange(
        GameId gameId, Stream iso, IsoPatchPlan plan, int rangeIndex,
        CancellationToken cancellationToken = default)
    {
        if (gameId == GameId.UYA) UyaIsoPatchApplier.ApplyRange(iso, plan, rangeIndex, cancellationToken);
        else throw Unsupported(gameId);
    }

    public static void VerifyRangeOutput(
        GameId gameId, Stream iso, IsoPatchPlan plan, int rangeIndex,
        CancellationToken cancellationToken = default)
    {
        if (gameId == GameId.UYA)
            UyaIsoPatchApplier.VerifyRangeOutput(iso, plan, rangeIndex, cancellationToken);
        else throw Unsupported(gameId);
    }

    public static void VerifyOutput(
        GameId gameId, Stream iso, IsoPatchPlan plan, CancellationToken cancellationToken = default)
    {
        if (gameId == GameId.UYA) UyaIsoPatchApplier.VerifyOutput(iso, plan, cancellationToken);
        else throw Unsupported(gameId);
    }

    public static void VerifyInstalledLevel(
        GameId gameId, Stream iso, IsoPatchPlan plan, CancellationToken cancellationToken = default)
    {
        if (gameId == GameId.UYA) UyaIsoPatchApplier.VerifyInstalledLevel(iso, plan, cancellationToken);
        else throw Unsupported(gameId);
    }

    public static string HashRange(
        GameId gameId, Stream stream, long offset, int length, CancellationToken cancellationToken = default) =>
        gameId == GameId.UYA
            ? UyaIsoPatchApplier.HashRange(stream, offset, length, cancellationToken)
            : throw Unsupported(gameId);

    private static NotSupportedException Unsupported(GameId gameId) =>
        new($"ISO patch application is not supported for {gameId}.");
}
