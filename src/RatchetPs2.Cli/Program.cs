using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Commands;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Cli.Routing;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL;
using RatchetPs2.Games.GC;
using RatchetPs2.Games.RC1;
using RatchetPs2.Games.UYA;

var gameModules = new IGameModule[]
{
    new RC1GameModule(),
    new GCGameModule(),
    new UYAGameModule(),
    new DLGameModule()
};

var gameModuleResolver = new GameModuleResolver(gameModules);

var commands = new ICommand[]
{
    new HelloCommand(gameModuleResolver)
};

var router = new CommandRouter(commands);
return router.Invoke(args);