using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class FinanceExpenseAiRequest
{
    [Required]
    public Guid ProjectId { get; init; }

    [Required]
    public required string PeriodStart { get; init; }

    [Required]
    public required string PeriodEnd { get; init; }
}

public sealed class FinanceExpenseAiResponse
{
    public required string Narrative { get; init; }

    /// <summary><c>openai</c> when the model ran; <c>heuristic</c> when no API key or call failed.</summary>
    public required string Source { get; init; }
}
