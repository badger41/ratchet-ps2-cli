using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Disc;
using RatchetPs2.Games.DL.Disc;
using RatchetPs2.Games.GC.Disc;
using RatchetPs2.Games.RC1.Disc;

namespace RatchetPs2.Sdk;

public static class DiscImageInspector
{
    public static DiscImageProfile GetSupportedProfile(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaDiscImageInspector.SupportedProfile,
        GameId.DL => DlDiscImageInspector.SupportedProfile,
        GameId.GC => GcDiscImageInspector.SupportedProfile,
        GameId.RC1 => Rc1DiscImageInspector.SupportedProfile,
        _ => throw new NotSupportedException($"Disc profiles are not supported for {gameId}."),
    };

    public static DiscImageInfo Inspect(GameId gameId, Stream source) => gameId switch
    {
        GameId.UYA => UyaDiscImageInspector.Inspect(source),
        GameId.DL => DlDiscImageInspector.Inspect(source),
        GameId.GC => GcDiscImageInspector.Inspect(source),
        GameId.RC1 => Rc1DiscImageInspector.Inspect(source),
        _ => throw new NotSupportedException($"Disc inspection is not supported for {gameId}."),
    };
}
