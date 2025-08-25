using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using YAMOH.Commands;
using YAMOH.Infrastructure;

namespace YAMOH.Services;

public class SchedulerService(
    IServiceProvider serviceProvider,
    IOptions<ScheduleOptions> options,
    ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Scheduler is disabled. Check settings to reenable");
            return;
        }

        var runNextLoop = options.Value.RunOnStartup;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (runNextLoop)
                {
                    using var scope = serviceProvider.CreateScope();
                    var command = scope.ServiceProvider.GetRequiredService<OverlayManagerCommand>();
                    await command.RunAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled command failed");
            }

            if (options.Value.ScheduleType == ScheduleType.Interval)
            {
                AnsiConsole.Status().Spinner(Spinner.Known.BouncingBall).Start("Awaiting next run...", ctx =>
                {
                    var seconds = int.TryParse(options.Value.Value, out var s) ? s : 300;
                    for (var i = seconds; i > 0; i--)
                    {
                        var color = "[cyan]";
                        var percentageLeft = (double)i / (double)seconds;

                        if (percentageLeft < 0.10)
                        {
                            color = "[red]";
                        }

                        ctx.Status = $"\r{color}Next run in {i} seconds...[/]";

                        Thread.Sleep(1000);
                        //await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        if (stoppingToken.IsCancellationRequested) break;
                    }
                    AnsiConsole.WriteLine();
                });
                runNextLoop = true;
            }
            // Add cron support here if needed
        }
    }
}
