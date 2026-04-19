namespace C2E.Api.Models;

public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Declined = 3,
    Expired = 4,
}

public sealed class ClientQuote
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public required string ReferenceNumber { get; set; }
    public required string Title { get; set; }
    public string? ScopeSummary { get; set; }

    public decimal EstimatedHours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TotalAmount { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateOnly? ValidThrough { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
