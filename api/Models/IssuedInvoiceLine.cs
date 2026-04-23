namespace C2E.Api.Models;

public sealed class IssuedInvoiceLine
{
    public Guid Id { get; set; }

    public Guid IssuedInvoiceId { get; set; }
    public IssuedInvoice? IssuedInvoice { get; set; }

    public Guid? ExpenseEntryId { get; set; }
    public ExpenseEntry? ExpenseEntry { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}
