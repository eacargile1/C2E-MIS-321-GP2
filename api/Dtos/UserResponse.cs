namespace C2E.Api.Dtos;

public class UserResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
    public Guid? ManagerUserId { get; init; }
}
