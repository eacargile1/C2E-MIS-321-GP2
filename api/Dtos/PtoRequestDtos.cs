using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class CreatePtoRequestDto
{
    [Required]
    public required string StartDate { get; init; }

    [Required]
    public required string EndDate { get; init; }

    [MaxLength(2000)]
    public string? Reason { get; init; }
}

public sealed class PtoRequestResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string StartDate { get; init; }
    public required string EndDate { get; init; }
    public required string Reason { get; init; }
    public required string Status { get; init; }
    public required Guid ApproverUserId { get; init; }
    public required string ApproverEmail { get; init; }
    public Guid? SecondaryApproverUserId { get; init; }
    public string? SecondaryApproverEmail { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public Guid? ReviewedByUserId { get; init; }
}
