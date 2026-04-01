namespace RatchetPs2.Core.Games;

public interface IGameModule
{
    GameId Id { get; }
    string DisplayName { get; }
}