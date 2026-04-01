using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.Commands;
using RatchetPs2.Cli.Routing;

var commands = new ICommand[]
{
    new HelloCommand()
};

var router = new CommandRouter(commands);
return router.Invoke(args);