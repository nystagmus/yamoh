namespace YAMOH.Infrastructure;

public interface IYamohCommand
{
    string CommandName { get; }

    string CommandDescription { get; }

    Task RunAsync(CancellationToken cancellationToken = default);
}
