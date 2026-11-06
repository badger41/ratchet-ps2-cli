using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelContentInventoryReader
{
    public static IReadOnlyList<LevelContentFamily> Read(byte[] levelWad, CancellationToken cancellationToken = default) =>
        UyaLevelContentInventoryReader.ReadConverted(Rc1LevelContentConverter.ConvertToUya(levelWad, cancellationToken), GameId.RC1, cancellationToken);
}
