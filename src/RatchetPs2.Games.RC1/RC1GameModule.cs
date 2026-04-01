using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.RC1;

public sealed class RC1GameModule : IGameModule
{
    public GameId Id => GameId.RC1;
    public string DisplayName => "Ratchet & Clank";
}