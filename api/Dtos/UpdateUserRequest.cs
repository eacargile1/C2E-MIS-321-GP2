using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public class UpdateUserRequest
{
    [EmailAddress]
    public string? Email { get; init; }

    [MinLength(8)]
    public string? Password { get; init; }

    public bool? IsActive { get; init; }

    public string? Role { get; init; }
}
