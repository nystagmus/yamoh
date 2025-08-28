using Microsoft.Extensions.DependencyInjection;
using YAMOH.Infrastructure.Scheduling;

namespace YAMOH.Features.OverlayManager;

public class OverlayManagerJob(IServiceProvider serviceProvider) : ICronJob
{
    public async Task Run(CancellationToken stoppingToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<OverlayManagerCommand>();
        await command.RunAsync(stoppingToken);
    }
}