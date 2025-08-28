using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using YAMOH.Infrastructure.Extensions;

namespace YAMOH.Services;

public class CronScheduler(
    IServiceProvider serviceProvider,
    IEnumerable<CronRegistryEntry> cronJobs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var cronJob in cronJobs)
        {
            var cronDescription = cronJob.CrontabSchedule.ToDescriptor();
            var typeName = cronJob.Type.Name;

            AnsiConsole.MarkupLine(
                $"[bold green]Scheduler found job [/][bold yellow]{typeName}[/][bold green] running[/][bold blue] {cronDescription}[/]");
        }

        await AnsiConsole.Status().StartAsync("[green]Starting Scheduler...[/]", async ctx =>
        {
            // Create a map of the next upcoming entries
            var runMap = GetJobRuns();

            ctx.Spinner(Spinner.Known.Star);
            ctx.SpinnerStyle(Style.Parse("green"));
            ctx.Status(GetNextJobRunString(runMap));

            // Create a timer that has a resolution less than 60 seconds
            // Because cron has a resolution of a minute
            // So everything under will work
            using var tickTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));

            var lastReportTime = DateTime.UtcNow;

            while (await tickTimer.WaitForNextTickAsync(stoppingToken))
            {
                // Get UTC Now with minute resolution (remove microseconds and seconds)
                var now = UtcNowMinutePrecision();

                // Run jobs that are in the map
                RunActiveJobs(runMap, now, stoppingToken);

                // Get the next run for the upcoming tick
                runMap = GetJobRuns();

                var timeSinceLastReport = now - lastReportTime;
                var nextRunDeltaTicks = (runMap.OrderBy(x => x.Key).First().Key - now).Ticks;
                var reportInterval = Math.Max(nextRunDeltaTicks / 100, TimeSpan.FromSeconds(30).Ticks); // report less frequently the further out it is.

                if (timeSinceLastReport.Ticks <= reportInterval)
                {
                    continue;
                }

                // Update status
                lastReportTime = now;
                ctx.Status(GetNextJobRunString(runMap));
            }
        });
    }

    private string GetNextJobRunString(IReadOnlyDictionary<DateTime, List<Type>> runMap)
    {
        var utcNow = DateTime.UtcNow;

        if (runMap.Count == 0)
        {
            return "[red]No jobs scheduled. Check your config.[/]";
        }

        var nextRun = runMap.OrderBy(x => x.Key).First();
        var nextRunNames = string.Join(',', nextRun.Value.Select(x => x.Name));
        var remainingTime = nextRun.Key.Subtract(utcNow).ToSmartString();

        return
            $"[green]Waiting to run [/][yellow]{nextRunNames}[/][green] at [/][blue]{nextRun.Key} [/][green](in approx {remainingTime})[/]";
    }

    private void RunActiveJobs(IReadOnlyDictionary<DateTime, List<Type>> runMap, DateTime now,
        CancellationToken stoppingToken)
    {
        if (!runMap.TryGetValue(now, out var currentRuns))
        {
            return;
        }

        foreach (var run in currentRuns)
        {
            // We are sure (thanks to our extension method)
            // that the service is of type ICronJob
            var job = (ICronJob)serviceProvider.GetRequiredService(run);

            // We don't want to await jobs explicitly because that
            // could interfere with other job runs
            job.Run(stoppingToken);
        }
    }

    private Dictionary<DateTime, List<Type>> GetJobRuns()
    {
        var runMap = new Dictionary<DateTime, List<Type>>();

        foreach (var cron in cronJobs)
        {
            var utcNow = DateTime.UtcNow;
            var runDate = cron.CrontabSchedule.GetNextOccurrence(utcNow);

            if (runDate != null)
                AddJobRun(runMap, runDate.Value, cron);
        }

        return runMap;
    }

    private static void AddJobRun(IDictionary<DateTime, List<Type>> runMap, DateTime runDate, CronRegistryEntry cron)
    {
        if (runMap.TryGetValue(runDate, out var value))
        {
            value.Add(cron.Type);
        }
        else
        {
            runMap[runDate] = [cron.Type];
        }
    }

    private static DateTime UtcNowMinutePrecision()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
    }
}
