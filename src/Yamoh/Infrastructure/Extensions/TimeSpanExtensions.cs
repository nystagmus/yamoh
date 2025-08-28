namespace Yamoh.Infrastructure.Extensions;

public static class TimeSpanExtensions
{
    public static string ToSmartString(this TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
        }

        if (ts.TotalHours >= 1)
        {
            return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        }

        return ts.TotalMinutes >= 1 ? $"{ts.Minutes}m {ts.Seconds}s" : $"{ts.Seconds}s";
    }
}