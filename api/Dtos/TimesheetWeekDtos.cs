using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class TimesheetLineUpsertRequest
{
    [Required]
    public required string WorkDate { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Client { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Project { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Task { get; init; }

    [Range(typeof(decimal), "0.0000001", "24")]
    public decimal Hours { get; init; }

    public bool IsBillable { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

public sealed class TimesheetLineResponse
{
    public required string WorkDate { get; init; }
    public required string Client { get; init; }
    public required string Project { get; init; }
    public required string Task { get; init; }
    public required decimal Hours { get; init; }
    public required bool IsBillable { get; init; }
    public string? Notes { get; init; }
}

public sealed class ResourceTrackerDayResponse
{
    public required string Date { get; init; }
    public required string Status { get; init; } // Available | SoftBooked | FullyBooked | PTO
    public required decimal Hours { get; init; }
}

public sealed class ResourceTrackerEmployeeRowResponse
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required IReadOnlyList<ResourceTrackerDayResponse> Days { get; init; }
}

public sealed class TimesheetWeekStatusResponse
{
    public required string WeekStart { get; init; }
    /// <summary>None, Pending, Approved, or Rejected (None when no submission exists).</summary>
    public required string Status { get; init; }
    /// <summary>All hours on the timesheet grid for this week (current).</summary>
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    /// <summary>When <see cref="Status"/> is Pending: hours captured at submit (what reviewers evaluate). Null if legacy row.</summary>
    public decimal? PendingSubmissionTotalHours { get; init; }
    /// <summary>When Status is Pending: billable hours at submit. Null if legacy row.</summary>
    public decimal? PendingSubmissionBillableHours { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
}

public sealed class PendingTimesheetWeekResponse
{
    public required Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string WeekStart { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required DateTime SubmittedAtUtc { get; init; }
}

/// <summary>Budget context for a catalog-matched project on the IC review screen.</summary>
public sealed class ProjectBudgetBarDto
{
    public required string ClientName { get; init; }
    public required string ProjectName { get; init; }
    public decimal BudgetAmount { get; init; }
    public decimal? DefaultHourlyRate { get; init; }
    /// <summary>
    /// Billable dollars already counted: non-IC billable lines on this project, plus IC lines only in manager-approved weeks.
    /// </summary>
    public decimal ConsumedBillableAmount { get; init; }
    /// <summary>Billable dollars from this pending submission for this client/project.</summary>
    public decimal PendingSubmissionBillableAmount { get; init; }
    public decimal PendingBillableHours { get; init; }
    public bool CatalogMatched { get; init; }
}

/// <summary>IC lines + metadata for manager/admin review of a pending weekly submission.</summary>
public sealed class TimesheetPendingWeekReviewResponse
{
    public required Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string WeekStart { get; init; }
    public required DateTime SubmittedAtUtc { get; init; }
    public required IReadOnlyList<TimesheetLineResponse> Lines { get; init; }
    public IReadOnlyList<ProjectBudgetBarDto> ProjectBudgetBars { get; init; } = [];
}

