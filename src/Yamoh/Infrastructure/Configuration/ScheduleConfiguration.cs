using Spectre.Console;

namespace Yamoh.Infrastructure.Configuration;

public class ScheduleConfiguration
{
    public static string Position => "Schedule";

    public bool Enabled { get; init; }
    public bool RunOnStartup { get; init; }
    public string OverlayManagerCronSchedule { get; init; } = "30 * * * *";
}