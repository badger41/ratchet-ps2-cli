using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.GameSelection;

internal sealed class GameModuleResolver
{
    private readonly IReadOnlyDictionary<GameId, IGameModule> _modules;

    public GameModuleResolver(IEnumerable<IGameModule> modules)
    {
        _modules = modules.ToDictionary(module => module.Id);
    }

    public IGameModule Resolve(GameId gameId)
    {
        if (_modules.TryGetValue(gameId, out var module))
        {
            return module;
        }

        throw new InvalidOperationException($"No game module registered for '{gameId}'.");
    }
}