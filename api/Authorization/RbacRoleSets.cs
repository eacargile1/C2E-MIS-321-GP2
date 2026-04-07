using C2E.Api.Models;

namespace C2E.Api.Authorization;

/// <summary>
/// Maps PRD RBAC matrix rows to role strings for <c>[Authorize(Roles = ...)]</c>.
/// Use with <see cref="AppRole"/> enum names (JWT <see cref="System.Security.Claims.ClaimTypes.Role"/>).
/// </summary>
public static class RbacRoleSets
{
    /// <summary>Org-wide timesheets (FR10), employee billing rates read, generate invoice (stub).</summary>
    public const string AdminAndFinance = $"{nameof(AppRole.Admin)},{nameof(AppRole.Finance)}";

    /// <summary>Create client (stub); matrix: Admin-only for client create/edit.</summary>
    public const string AdminOnly = nameof(AppRole.Admin);

    /// <summary>Project create/edit and staffing actions allowed for Admin + Manager.</summary>
    public const string AdminAndManager = $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)}";
}
