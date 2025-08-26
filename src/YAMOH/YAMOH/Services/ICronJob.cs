namespace YAMOH.Services;

public interface ICronJob
{
    Task Run(CancellationToken stoppingToken = default);
}