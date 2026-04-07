namespace C2E.Api.Models;

public enum ExpenseStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

public sealed class ExpenseEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly ExpenseDate { get; set; }

    public required string Client { get; set; }
    public required string Project { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }

    public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
