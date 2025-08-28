using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace YAMOH.Infrastructure;

public class CommandFactory(IEnumerable<IYamohCommand> yamohCommands, IServiceScopeFactory scopeFactory)
{
    public IList<Command> GenerateCommandTree()
    {
        var commands = new List<Command>();
        foreach (var yamohCommand in yamohCommands)
        {
            var commandType = yamohCommand.GetType();
            var command = new Command(yamohCommand.CommandName, yamohCommand.CommandDescription);

            command.SetAction(async (parseResult, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();

                var resolvedCommand = (IYamohCommand)scope.ServiceProvider.GetRequiredService(commandType);
                await resolvedCommand.RunAsync(cancellationToken);
            });
            commands.Add(command);
        }
        return commands;
    }
}
