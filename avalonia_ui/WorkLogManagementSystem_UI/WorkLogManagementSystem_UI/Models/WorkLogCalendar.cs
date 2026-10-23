using System;

namespace WorkLogManagementSystem_UI.Models;

/// <summary>
/// Calendar dates use the backend's fixed Korean timezone, not the device timezone.
/// Keep the same year-1/year-9999 sentinels as the backend DateBounds class.
/// </summary>
public static class WorkLogCalendar
{
    public static readonly TimeSpan TimezoneOffset = TimeSpan.FromHours(9);

    public static DateTime Today => ToDate(DateTimeOffset.UtcNow);

    public static DateTime ToDate(DateTimeOffset value)
    {
        if (value.Year <= 1)
        {
            return DateTime.MinValue;
        }

        if (value.Year >= 9999)
        {
            return DateTime.MaxValue;
        }

        return value.ToOffset(TimezoneOffset).Date;
    }

    public static DateTimeOffset ToRequestDate(DateTime? value)
    {
        DateTime date = (value ?? Today).Date;
        if (date.Year <= 1)
        {
            return DateTimeOffset.MinValue;
        }

        if (date.Year >= 9999)
        {
            return new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimezoneOffset);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimezoneOffset);
    }
}
