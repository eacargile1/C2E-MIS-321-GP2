namespace C2E.Api.Dtos;

public sealed class TimesheetProjectRefResponse
{
    public required string Client { get; init; }
    public required string Project { get; init; }
    public string? LastWorkedOn { get; init; } // YYYY-MM-DD
}

