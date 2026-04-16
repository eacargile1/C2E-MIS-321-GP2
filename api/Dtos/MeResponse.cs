namespace C2E.Api.Dtos;

public class MeResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
}
