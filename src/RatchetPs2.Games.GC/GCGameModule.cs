using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.GC;

public sealed class GCGameModule : IGameModule
{
    public GameId Id => GameId.GC;
    public string DisplayName => "Going Commando";
}