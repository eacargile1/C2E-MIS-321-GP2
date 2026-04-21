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

public sealed class PersonalDetailProjectRow
{
    public required string Client { get; init; }
    public required string Project { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required decimal NonBillableHours { get; init; }
}

public sealed class PersonalDetailResponse
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required IReadOnlyList<PersonalDetailProjectRow> Rows { get; init; }
}

public sealed class TeamMemberSummaryRow
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required decimal NonBillableHours { get; init; }
    public required int TimesheetLineCount { get; init; }
    public required int ExpenseCount { get; init; }
    public required decimal ExpensePendingTotal { get; init; }
    public required decimal ExpenseApprovedTotal { get; init; }
}

public sealed class TeamSummaryResponse
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required IReadOnlyList<TeamMemberSummaryRow> Rows { get; init; }
}
