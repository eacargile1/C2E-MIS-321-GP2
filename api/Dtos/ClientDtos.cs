using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class ClientResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? ContactName { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    /// <summary>Present only for Admin/Finance callers.</summary>
    public decimal? DefaultBillingRate { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Placeholder until projects (E4) exist — PRD FR33/FR34.</summary>
    public IReadOnlyList<ClientProjectStubDto> Projects { get; init; } = [];
}

public sealed class ClientProjectStubDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
}

public sealed class CreateClientRequest
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(200)]
    public string? ContactName { get; init; }

    [MaxLength(320)]
    [EmailAddress]
    public string? ContactEmail { get; init; }

    [MaxLength(50)]
    public string? ContactPhone { get; init; }

    [Range(0, 100000)]
    public decimal? DefaultBillingRate { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public sealed class PatchClientRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(200)]
    public string? ContactName { get; init; }

    [MaxLength(320)]
    [EmailAddress]
    public string? ContactEmail { get; init; }

    [MaxLength(50)]
    public string? ContactPhone { get; init; }

    [Range(0, 100000)]
    public decimal? DefaultBillingRate { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public bool? IsActive { get; init; }
}
