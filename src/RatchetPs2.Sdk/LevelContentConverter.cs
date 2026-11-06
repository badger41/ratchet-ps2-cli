using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelContentConverter
{
    public static LevelContentConversion ExpandTieModels(GameId targetGame, LevelContentConversion conversion,
        IReadOnlyDictionary<int, IReadOnlyList<int>> classes, CancellationToken cancellationToken = default) => targetGame switch
    {
        GameId.UYA => UyaLevelContentModelExpansion.ExpandTies(conversion, classes, cancellationToken),
        _ => throw new NotSupportedException($"Split Tie instances are not supported for {targetGame}."),
    };

    public static LevelContentConversion Convert(
        GameId sourceGame, GameId targetGame, byte[] levelWad,
        CancellationToken cancellationToken = default) => (sourceGame, targetGame) switch
    {
        (GameId.RC1, GameId.UYA) => Rc1LevelContentConverter.ConvertToUya(levelWad, cancellationToken),
        (GameId.GC, GameId.UYA) => GcLevelContentConverter.ConvertToUya(levelWad, cancellationToken),
        (GameId.DL, GameId.UYA) => UyaDlLevelContentConverter.Convert(levelWad, cancellationToken),
        _ => throw new NotSupportedException($"Content conversion from {sourceGame} to {targetGame} is not supported."),
    };
}
