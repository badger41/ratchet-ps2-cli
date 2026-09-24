using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class LevelArchiveBuilder
{
    public const int SchemaVersion = 1;

    public static LevelArchiveCapability GetCapability(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaLevelArchiveBuilder.Capability,
        _ => throw new NotSupportedException($"Level archive builds are not supported for {gameId}.")
    };

    public static bool SupportsTarget(string game, string region, string revision, string bakeProfile) =>
        Enum.TryParse<GameId>(game, ignoreCase: true, out var gameId)
        && gameId switch
        {
            GameId.UYA => UyaLevelArchiveBuilder.SupportsTarget(game, region, revision, bakeProfile),
            _ => false,
        };

    public static LevelArchiveBuildResult Build(
        GameId gameId,
        ReadOnlyMemory<byte> source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null,
        LevelArchiveBuildOptions? options = null,
        IProgress<LevelArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaLevelArchiveBuilder.Build(source, replacements, options, progress, cancellationToken),
            _ => throw new NotSupportedException($"Level archive builds are not supported for {gameId}."),
        };

    public static LevelArchiveBuildResult Build(
        GameId gameId,
        Stream source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? replacements = null,
        LevelArchiveBuildOptions? options = null,
        IProgress<LevelArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaLevelArchiveBuilder.Build(source, replacements, options, progress, cancellationToken),
            _ => throw new NotSupportedException($"Level archive builds are not supported for {gameId}."),
        };
}
