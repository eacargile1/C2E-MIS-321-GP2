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

    public Guid? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}
