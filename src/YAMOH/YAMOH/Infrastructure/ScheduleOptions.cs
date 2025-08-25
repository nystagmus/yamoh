namespace YAMOH.Infrastructure;

public class ScheduleOptions
{
    public const string Position = "Schedule";

    public bool Enabled { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Interval;
    public string Value { get; set; } = "300";
}