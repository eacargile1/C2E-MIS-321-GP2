using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace C2E.Api.Dtos;

public sealed class CreateExpenseRequest
{
    [Required]
    public required string ExpenseDate { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Client { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Project { get; init; }

    [Required]
    [MaxLength(80)]
    public required string Category { get; init; }

    [Required]
    [MaxLength(500)]
    public required string Description { get; init; }

    [Range(typeof(decimal), "0.01", "99999999")]
    public decimal Amount { get; init; }
}

/// <summary>Multipart create (same fields as JSON + optional invoice file).</summary>
public sealed class CreateExpenseFormRequest
{
    [Required]
    public string ExpenseDate { get; set; } = "";

    [Required]
    [MaxLength(120)]
    public string Client { get; set; } = "";

    [Required]
    [MaxLength(120)]
    public string Project { get; set; } = "";

    [Required]
    [MaxLength(80)]
    public string Category { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = "";

    [Range(typeof(decimal), "0.01", "99999999")]
    public decimal Amount { get; set; }

    public IFormFile? Invoice { get; set; }
}

public sealed class ExpenseResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string ExpenseDate { get; init; }
    public required string Client { get; init; }
    public required string Project { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public required string Status { get; init; }
    public string? ReviewedByEmail { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public bool HasInvoice { get; init; }
}
