using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.DL;

public sealed class DLGameModule : IGameModule
{
    public GameId Id => GameId.DL;
    public string DisplayName => "Deadlocked";
}