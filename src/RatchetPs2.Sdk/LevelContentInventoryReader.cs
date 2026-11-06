using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelContentInventoryReader
{
    public static IReadOnlyList<LevelContentFamily> Read(
        GameId game, byte[] levelWad, CancellationToken cancellationToken = default) => game switch
    {
        GameId.RC1 => Rc1LevelContentInventoryReader.Read(levelWad, cancellationToken),
        GameId.GC => GcLevelContentInventoryReader.Read(levelWad, cancellationToken),
        GameId.DL => ReadConverted(GameId.DL, GameId.UYA, LevelContentConverter.Convert(GameId.DL, GameId.UYA, levelWad, cancellationToken), cancellationToken),
        _ => throw new NotSupportedException($"Content inventory is not supported for {game}."),
    };

    public static IReadOnlyList<LevelContentFamily> ReadConverted(GameId sourceGame, GameId targetGame,
        LevelContentConversion conversion, CancellationToken cancellationToken = default) => targetGame switch
    {
        GameId.UYA => UyaLevelContentInventoryReader.ReadConverted(conversion, sourceGame, cancellationToken),
        _ => throw new NotSupportedException($"Converted content inventory is not supported for {targetGame}."),
    };
}
