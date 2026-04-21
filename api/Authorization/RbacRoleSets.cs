using C2E.Api.Models;

namespace C2E.Api.Authorization;

/// <summary>
/// Maps PRD RBAC matrix rows to role strings for <c>[Authorize(Roles = ...)]</c>.
/// Use with <see cref="AppRole"/> enum names (JWT <see cref="System.Security.Claims.ClaimTypes.Role"/>).
/// </summary>
public static class RbacRoleSets
{
    /// <summary>Org-wide timesheets (FR10), finance register/quotes read, employee billing rates read.</summary>
    public const string AdminFinanceManager =
        $"{nameof(AppRole.Admin)},{nameof(AppRole.Finance)},{nameof(AppRole.Manager)}";

    /// <summary>Finance operations requiring Finance/Admin authority (e.g., quote create, invoice generate).</summary>
    public const string AdminAndFinance = $"{nameof(AppRole.Admin)},{nameof(AppRole.Finance)}";

    /// <summary>Create client (stub); matrix: Admin-only for client create/edit.</summary>
    public const string AdminOnly = nameof(AppRole.Admin);

    /// <summary>Approval/review actions owned by delivery leadership (Admin + Manager).</summary>
    public const string AdminAndManager = $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)}";

    /// <summary>Staffing assignment actions reserved for leadership with client ownership authority.</summary>
    public const string AdminAndPartner = $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)}";

    /// <summary>Resource planning / forecast UX tier (Partner joins delivery leadership).</summary>
    public const string AdminManagerPartner = $"{nameof(AppRole.Admin)},{nameof(AppRole.Manager)},{nameof(AppRole.Partner)}";

    /// <summary>Client directory create (IC excluded; managers escalate to Partner/Finance).</summary>
    public const string AdminPartnerFinance = $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)},{nameof(AppRole.Finance)}";

    /// <summary>Project create and staffing assignment (Admin + Partner only).</summary>
    public const string AdminPartner = $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)}";

    /// <summary>Read-only staffing directory for labels on project views (IC browse-only + delivery roles).</summary>
    public const string ProjectStaffingDirectoryReaders =
        $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)},{nameof(AppRole.Manager)},{nameof(AppRole.Finance)},{nameof(AppRole.IC)}";

    /// <summary>Project PATCH: full edit (Admin + Partner) or budget-only for Finance assigned on the project.</summary>
    public const string AdminPartnerFinanceProjectPatch =
        $"{nameof(AppRole.Admin)},{nameof(AppRole.Partner)},{nameof(AppRole.Finance)}";
}
