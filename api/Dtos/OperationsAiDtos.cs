using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class OperationsAiInsightDto
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Source { get; init; }
}

public sealed class OperationsExpenseAiReviewRequest
{
    [Required]
    public string ExpenseDate { get; init; } = "";

    [Required]
    [MaxLength(120)]
    public string Client { get; init; } = "";

    [Required]
    [MaxLength(120)]
    public string Project { get; init; } = "";

    [Required]
    [MaxLength(80)]
    public string Category { get; init; } = "";

    [Required]
    [MaxLength(500)]
    public string Description { get; init; } = "";

    [Range(typeof(decimal), "0.01", "99999999")]
    public decimal Amount { get; init; }

    public bool HasInvoiceAttachment { get; init; }
}

public sealed class OperationsExpenseAiReviewResponse
{
    /// <summary>draft | approver</summary>
    public string ReviewKind { get; init; } = "draft";

    public string? SubmitterEmail { get; init; }
    public required bool UsedLlm { get; init; }
    public string? LlmNote { get; init; }
    public required IReadOnlyList<OperationsAiInsightDto> Insights { get; init; }
    public required IReadOnlyList<string> QuestionsForSubmitter { get; init; }
}

public sealed class OperationsExpenseApproverReviewRequest
{
    [Required]
    public Guid ExpenseId { get; init; }
}

public sealed class OperationsTimesheetApproverReviewRequest
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    public string WeekStartMonday { get; init; } = "";
}

public sealed class OperationsTimesheetAiLineDto
{
    [Required]
    public string WorkDate { get; init; } = "";

    [Required]
    [MaxLength(120)]
    public string Client { get; init; } = "";

    [Required]
    [MaxLength(120)]
    public string Project { get; init; } = "";

    [Required]
    [MaxLength(120)]
    public string Task { get; init; } = "";

    [Range(typeof(decimal), "0", "24")]
    public decimal Hours { get; init; }

    public bool IsBillable { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public sealed class OperationsTimesheetWeekAiReviewRequest
{
    [Required]
    public string WeekStartMonday { get; init; } = "";

    public List<OperationsTimesheetAiLineDto> Lines { get; init; } = [];
}

public sealed class OperationsTimesheetWeekAiReviewResponse
{
    /// <summary>draft | approver</summary>
    public string ReviewKind { get; init; } = "draft";

    public string? SubjectEmail { get; init; }
    public required bool UsedLlm { get; init; }
    public string? LlmNote { get; init; }
    public decimal WeekTotalHours { get; init; }
    public required IReadOnlyList<OperationsAiInsightDto> Insights { get; init; }
    public required IReadOnlyList<string> QuestionsForEmployee { get; init; }
    public required IReadOnlyList<string> NoteSuggestions { get; init; }
}

public sealed class FinanceLedgerAuditRequest
{
    [MaxLength(200)]
    public string? EmployeeEmailContains { get; init; }

    [MaxLength(120)]
    public string? ClientNameContains { get; init; }

    [Range(1, 200)]
    public int MaxRows { get; init; } = 100;
}

public sealed class FinanceLedgerAuditResponse
{
    public required bool UsedLlm { get; init; }
    public string? LlmNote { get; init; }
    public int RowCount { get; init; }
    public decimal TotalPendingAmount { get; init; }
    public decimal TotalApprovedAmount { get; init; }
    public required IReadOnlyList<OperationsAiInsightDto> Insights { get; init; }
    public required IReadOnlyList<string> SummaryPoints { get; init; }
}

public sealed class FinanceQuoteDraftRequest
{
    [Required]
    public Guid ClientId { get; init; }

    [MaxLength(320)]
    public string? ContextEmployeeEmail { get; init; }
}

public sealed class FinanceQuoteDraftResponse
{
    public required bool UsedLlm { get; init; }
    public string? LlmNote { get; init; }
    public string? SuggestedTitle { get; init; }
    public string? SuggestedScopeSummary { get; init; }
    public decimal? SuggestedHours { get; init; }
    public decimal? SuggestedHourlyRate { get; init; }
    public string? SuggestedValidThroughYmd { get; init; }
    public required IReadOnlyList<string> ReviewerChecklist { get; init; }
}
