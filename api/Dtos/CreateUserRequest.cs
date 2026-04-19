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

    /// <summary>Optional; defaults to the email local-part when omitted or whitespace.</summary>
    [MaxLength(80)]
    public string? DisplayName { get; init; }

    public Guid? ManagerUserId { get; init; }
}
