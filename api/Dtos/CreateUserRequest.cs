using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public class CreateUserRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    [MinLength(8)]
    public required string Password { get; init; }
}
