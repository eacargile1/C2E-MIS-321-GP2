namespace C2E.Api.Dtos;

public sealed class AssignmentResponse
{
    public Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
}
