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

    /// <summary>Resource planning / forecast UX tier (Partner joins delivery leadership).</summary>
    public const string AdminManagerPartner = $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)},{nameof(AppRole.Partner)}";

    /// <summary>Client directory create (IC excluded; managers escalate to Partner/Finance).</summary>
    public const string AdminPartnerFinance = $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)},{nameof(AppRole.Finance)}";

    /// <summary>Project create (IC excluded); managers can add projects but cannot PATCH catalog fields.</summary>
    public const string AdminManagerPartnerFinance =
        $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)},{nameof(AppRole.Partner)},{nameof(AppRole.Finance)}";

    /// <summary>IC accounts are limited to own timesheet + expense submission; no org-wide views or reporting modules.</summary>
    public const string NonIc =
        $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)},{nameof(AppRole.Partner)},{nameof(AppRole.Finance)}";
}
