using System.CommandLine;

namespace YAMOH.Commands;

public interface IYamohCommandBase
{
    string CommandName { get; }

    string CommandDescription { get; }

    Task Run(CancellationToken cancellationToken = default);
}
