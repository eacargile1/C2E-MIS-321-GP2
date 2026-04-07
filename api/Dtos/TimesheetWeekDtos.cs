using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class TimesheetLineUpsertRequest
{
    [Required]
    public required string WorkDate { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Client { get; init; }

    [Required]
    [MaxLength(120)]
    public required string Project { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Task { get; init; }

    [Range(typeof(decimal), "0.0000001", "24")]
    public decimal Hours { get; init; }

    public bool IsBillable { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

public sealed class TimesheetLineResponse
{
    public required string WorkDate { get; init; }
    public required string Client { get; init; }
    public required string Project { get; init; }
    public required string Task { get; init; }
    public required decimal Hours { get; init; }
    public required bool IsBillable { get; init; }
    public string? Notes { get; init; }
}

public sealed class ResourceTrackerDayResponse
{
    public required string Date { get; init; }
    public required string Status { get; init; } // Available | SoftBooked | FullyBooked | PTO
    public required decimal Hours { get; init; }
}

public sealed class ResourceTrackerEmployeeRowResponse
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required IReadOnlyList<ResourceTrackerDayResponse> Days { get; init; }
}

