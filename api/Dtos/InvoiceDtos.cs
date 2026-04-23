using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class DraftInvoiceRequest
{
    [Required]
    public required Guid ClientId { get; init; }

    [Required]
    public required string PeriodStart { get; init; }

    [Required]
    public required string PeriodEnd { get; init; }
}

public sealed class DraftInvoiceLineDto
{
    public required string Source { get; init; }
    public required string Description { get; init; }
    public required decimal Quantity { get; init; }
    public required string Unit { get; init; }
    public required decimal UnitRate { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class DraftInvoiceResponse
{
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required string PeriodStart { get; init; }
    public required string PeriodEnd { get; init; }
    public decimal? DefaultHourlyRate { get; init; }
    public required IReadOnlyList<DraftInvoiceLineDto> Lines { get; init; }
    public required decimal Subtotal { get; init; }
    public required string Note { get; init; }
}

public sealed class IssueProjectInvoiceRequest
{
    [Required]
    public Guid ProjectId { get; init; }

    [Required]
    public required string PeriodStart { get; init; }

    [Required]
    public required string PeriodEnd { get; init; }
}

public sealed class IssueProjectInvoiceResponse
{
    public required Guid InvoiceId { get; init; }
    public required string IssueNumber { get; init; }
    public required decimal TotalAmount { get; init; }
    public int LineCount { get; init; }
}

public sealed class IssuePayoutInvoicesResponse
{
    public required IReadOnlyList<IssueProjectInvoiceResponse> Invoices { get; init; }
}

public sealed class IssuedInvoiceListItemDto
{
    public required Guid Id { get; init; }
    public required string Kind { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required string ClientName { get; init; }
    public string? PayeeEmail { get; init; }
    public required string PeriodStart { get; init; }
    public required string PeriodEnd { get; init; }
    public required string IssueNumber { get; init; }
    public DateTime IssuedAtUtc { get; init; }
    public decimal TotalAmount { get; init; }
}
