using System.CommandLine;

namespace YAMOH.Commands;

public interface IYamohCommand
{
    string CommandName { get; }

    string CommandDescription { get; }

    Task RunAsync(CancellationToken cancellationToken = default);
}
