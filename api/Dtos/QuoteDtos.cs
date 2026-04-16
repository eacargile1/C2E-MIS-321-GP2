using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class QuoteResponse
{
    public required Guid Id { get; init; }
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required string ReferenceNumber { get; init; }
    public required string Title { get; init; }
    public string? ScopeSummary { get; init; }
    public decimal EstimatedHours { get; init; }
    public decimal HourlyRate { get; init; }
    public decimal TotalAmount { get; init; }
    public required string Status { get; init; }
    public string? ValidThrough { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class CreateQuoteRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string? ScopeSummary { get; set; }

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal EstimatedHours { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal HourlyRate { get; set; }

    /// <summary>YYYY-MM-DD; optional.</summary>
    [MaxLength(10)]
    public string? ValidThrough { get; set; }

    /// <summary>Draft or Sent.</summary>
    [MaxLength(24)]
    public string? Status { get; set; }
}
