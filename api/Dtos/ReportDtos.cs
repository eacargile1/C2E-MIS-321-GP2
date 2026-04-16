namespace C2E.Api.Dtos;

public sealed class PersonalSummaryResponse
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required decimal NonBillableHours { get; init; }
    public required int TimesheetLineCount { get; init; }
    public required decimal ExpensePendingTotal { get; init; }
    public required decimal ExpenseApprovedTotal { get; init; }
    public required decimal ExpenseRejectedTotal { get; init; }
    public required int ExpenseCount { get; init; }
}
