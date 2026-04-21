using System.Globalization;
using C2E.Api.Options;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

/// <summary>
/// Timesheet entry is limited to Monday weeks from roughly one calendar month before through one month after
/// the week that contains &quot;today&quot; (UTC), so users can backfill or plan a little ahead while staying bounded.
/// </summary>
public sealed class TimesheetWeekWindow(IOptions<TimesheetWeekWindowOptions> options)
{
    private DateOnly ResolveTodayUtc()
    {
        var raw = options.Value.AnchorDateUtc?.Trim();
        if (!string.IsNullOrEmpty(raw) &&
            DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fixedDay))
            return fixedDay;

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public DateOnly AnchorMondayUtc()
    {
        var today = ResolveTodayUtc();
        return MondayOfWeekContaining(today);
    }

    public (DateOnly MinMonday, DateOnly MaxMonday) AllowedWeekMondayRange()
    {
        var anchorMonday = AnchorMondayUtc();
        var min = MondayOfWeekContaining(anchorMonday.AddMonths(-1));
        var max = MondayOfWeekContaining(anchorMonday.AddMonths(1));
        return (min, max);
    }

    public bool IsWeekStartAllowed(DateOnly weekStartMonday)
    {
        if (weekStartMonday.DayOfWeek != DayOfWeek.Monday)
            return false;

        var (min, max) = AllowedWeekMondayRange();
        return weekStartMonday >= min && weekStartMonday <= max;
    }

    public static DateOnly MondayOfWeekContaining(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }
}
