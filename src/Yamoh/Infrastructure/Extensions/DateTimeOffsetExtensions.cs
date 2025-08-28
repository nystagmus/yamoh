namespace Yamoh.Infrastructure.Extensions;

public static class DateTimeOffsetExtensions
{
    public static string GetDaySuffix(this DateTimeOffset dateTimeOffset)
    {
        var day = dateTimeOffset.Day;
        return day switch
        {
            1 or 21 or 31 => "st",
            2 or 22 => "nd",
            3 or 23 => "rd",
            _ => "th"
        };
    }
}