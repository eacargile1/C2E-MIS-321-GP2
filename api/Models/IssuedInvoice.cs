namespace C2E.Api.Models;

public sealed class IssuedInvoice
{
    public Guid Id { get; set; }

    public IssuedInvoiceKind Kind { get; set; }

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>For <see cref="IssuedInvoiceKind.UserPayout"/>; null for project rollup invoice.</summary>
    public Guid? PayeeUserId { get; set; }
    public AppUser? Payee { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public required string IssueNumber { get; set; }

    public DateTime IssuedAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
    public AppUser? IssuedBy { get; set; }

    public decimal TotalAmount { get; set; }

    public ICollection<IssuedInvoiceLine> Lines { get; set; } = new List<IssuedInvoiceLine>();
}
