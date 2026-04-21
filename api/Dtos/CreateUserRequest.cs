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

    /// <summary>Required for <c>IC</c>. For <c>Manager</c>, required when any Manager already exists (first Manager may omit).</summary>
    public Guid? ManagerUserId { get; init; }

    /// <summary>
    /// For <c>Finance</c>, optional: when omitted the first active Partner (by email) is assigned as reporting partner.
    /// For <c>Manager</c>, required when another Manager already exists (reporting partner).
    /// </summary>
    public Guid? PartnerUserId { get; init; }

    /// <summary>Optional; defaults to IC. Must match a known application role name (IC, Admin, Manager, Finance, Partner).</summary>
    public string? Role { get; init; }

    /// <summary>Optional list of user skills.</summary>
    public List<string>? Skills { get; init; }
}
