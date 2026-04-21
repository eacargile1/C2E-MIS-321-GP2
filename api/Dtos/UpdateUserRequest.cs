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

    [MaxLength(80)]
    public string? DisplayName { get; init; }

    /// <summary>When true, <see cref="ManagerUserId"/> is applied (including null to clear). When false/null, manager is unchanged.</summary>
    public bool? AssignManager { get; init; }

    public Guid? ManagerUserId { get; init; }

    /// <summary>When provided, replaces all user skills with this list.</summary>
    public List<string>? Skills { get; init; }
}
