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
