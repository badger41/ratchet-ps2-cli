using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Disc;

namespace RatchetPs2.Sdk;

public static class DiscImageInspector
{
    public static DiscImageProfile GetSupportedProfile(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaDiscImageInspector.SupportedProfile,
        _ => throw new NotSupportedException($"Disc profiles are not supported for {gameId}."),
    };

    public static DiscImageInfo Inspect(GameId gameId, Stream source) => gameId switch
    {
        GameId.UYA => UyaDiscImageInspector.Inspect(source),
        _ => throw new NotSupportedException($"Disc inspection is not supported for {gameId}."),
    };
}
