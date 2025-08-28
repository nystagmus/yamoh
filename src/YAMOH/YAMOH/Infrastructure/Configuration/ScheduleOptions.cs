namespace YAMOH.Infrastructure.Configuration;

public class ScheduleOptions
{
    public const string Position = "Schedule";

    public bool Enabled { get; init; }
    public bool RunOnStartup { get; init; }
    public string OverlayManagerCronSchedule { get; init; } = "30 * * * *";
}