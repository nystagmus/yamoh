namespace YAMOH.Infrastructure.Scheduling;

public interface ICronJob
{
    Task Run(CancellationToken stoppingToken = default);
}