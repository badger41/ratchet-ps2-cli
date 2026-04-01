namespace RatchetPs2.Cli.Abstractions;

internal interface ICommand
{
    string Name { get; }
    string Description { get; }
    int Invoke(IReadOnlyList<string> arguments);
}