using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class ProjectResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public decimal BudgetAmount { get; init; }
    public bool IsActive { get; init; }
}

public sealed class CreateProjectRequest
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    [Required]
    public Guid ClientId { get; init; }

    [Range(0, 100000000)]
    public decimal BudgetAmount { get; init; }
}

public sealed class PatchProjectRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    public Guid? ClientId { get; init; }

    [Range(0, 100000000)]
    public decimal? BudgetAmount { get; init; }

    public bool? IsActive { get; init; }
}
