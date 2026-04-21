namespace C2E.Api.Dtos;

public sealed class AssignmentResponse
{
    public Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
}

public sealed class StaffingRecommendationRequestDto
{
    public List<string> RequiredSkills { get; init; } = [];
}

public sealed class StaffingRecommendationResultDto
{
    public Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public decimal TotalScore { get; init; }
    public decimal SkillScore { get; init; }
    public decimal AvailabilityScore { get; init; }
    public decimal UtilizationScore { get; init; }
    public required List<string> Rationale { get; init; }
    public string? FallbackReason { get; init; }
}

public sealed class StaffingRecommendationResponseDto
{
    public required string FallbackMode { get; init; }
    public required List<StaffingRecommendationResultDto> Results { get; init; }
    public string? WarningMessage { get; init; }
}
