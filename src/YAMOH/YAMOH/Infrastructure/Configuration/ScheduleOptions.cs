namespace YAMOH.Infrastructure.Configuration;

public class ScheduleOptions
{
    public const string Position = "Schedule";

    public bool Enabled { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;
    public string OverlayManagerCronSchedule { get; set; } = "30 * * * *";
}