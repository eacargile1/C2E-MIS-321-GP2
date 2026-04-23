namespace C2E.Api.Models;

/// <summary>IC weekly timesheet submission for manager sign-off on billable hours.</summary>
public sealed class TimesheetWeekApproval
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Monday (inclusive) of the week in the user's calendar.</summary>
    public DateOnly WeekStartMonday { get; set; }

    public TimesheetWeekApprovalStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>Total hours on the timesheet grid when this submission was created (sign-off snapshot).</summary>
    public decimal? SubmittedTotalHours { get; set; }

    /// <summary>Billable hours on the grid when this submission was created.</summary>
    public decimal? SubmittedBillableHours { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}
