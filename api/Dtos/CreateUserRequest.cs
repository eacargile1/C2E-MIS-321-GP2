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

    /// <summary>Optional; defaults to IC. Must match a known application role name (IC, Admin, Manager, Finance, Partner).</summary>
    public string? Role { get; init; }
}
