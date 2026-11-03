using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Sdk;

public static class GameplayInstanceConverter
{
    public static byte[] RebaseMobyClass(GameId game, ReadOnlySpan<byte> source, int expectedClassId, int targetClassId) => game switch
    {
        GameId.UYA => UyaMobyInstancesWriter.RebaseClass(source, expectedClassId, targetClassId),
        _ => throw new NotSupportedException($"Moby instance class rebasing is not supported for {game}."),
    };
}
