namespace C2E.Api.Options;

public sealed class TimesheetWeekWindowOptions
{
    public const string SectionName = "Timesheets";

    /// <summary>Optional YYYY-MM-dd anchor for &quot;today&quot; (fixed clock for integration tests).</summary>
    public string? AnchorDateUtc { get; set; }
}
