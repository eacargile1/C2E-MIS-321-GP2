namespace C2E.Api.Models;

public sealed class TimesheetLine
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public DateOnly WorkDate { get; set; }

    public required string Client { get; set; }
    public required string Project { get; set; }
    public required string Task { get; set; }

    public decimal Hours { get; set; }
    public bool IsBillable { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

