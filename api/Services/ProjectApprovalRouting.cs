using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Services;

/// <summary>
/// Tiered billable approvals (timesheet weeks + client/project expenses): project
/// <see cref="Project.DeliveryManagerUserId"/> if set, else <see cref="Project.EngagementPartnerUserId"/>, else profile
/// superior — IC/Admin → <see cref="AppUser.ManagerUserId"/>; Finance → <see cref="AppUser.PartnerUserId"/>; Manager →
/// <see cref="AppUser.ManagerUserId"/>; Partner → self (<see cref="AppUser.Id"/>) so partner requests stay self-routed.
/// </summary>
public static class ProjectApprovalRouting
{
    /// <summary>Roles whose weekly billable sign-off is delivery manager (or org manager fallback).</summary>
    public static bool UsesDeliveryManagerWeekApproval(AppRole role) =>
        role is AppRole.IC or AppRole.Admin;

    /// <summary>Finance weekly billable sign-off uses the same project-first chain; fallback is reporting partner.</summary>
    public static bool UsesFinancePartnerWeekApproval(AppRole role) =>
        role == AppRole.Finance;

    /// <summary>Roles whose weekly billable sign-off uses manager/partner project-first routing (partner → self).</summary>
    public static bool UsesEngagementPartnerWeekApproval(AppRole role) =>
        role is AppRole.Manager or AppRole.Partner;

    public static async Task<Project?> FindActiveProjectByClientAndProjectName(
        AppDbContext db,
        string clientName,
        string projectName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(projectName))
            return null;

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.IsActive && c.Name.ToLower() == clientName.Trim().ToLowerInvariant())
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(ct);
        if (client is null) return null;

        return await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.IsActive &&
                     p.ClientId == client.Id &&
                     p.Name.ToLower() == projectName.Trim().ToLowerInvariant(),
                ct);
    }

    /// <summary>
    /// Effective reviewer for an IC/Admin expense line: project delivery manager, else engagement partner, else org manager.
    /// </summary>
    public static Guid? ResolveIcOrAdminBillableApproverId(Project? project, AppUser submitter)
    {
        if (project?.DeliveryManagerUserId is { } dm) return dm;
        if (project?.EngagementPartnerUserId is { } ep) return ep;
        return submitter.ManagerUserId;
    }

    /// <summary>Finance billable line reviewer: project DM, else EP, else reporting partner.</summary>
    public static Guid? ResolveFinanceBillableApproverId(Project? project, AppUser financeUser)
    {
        if (project?.DeliveryManagerUserId is { } dm) return dm;
        if (project?.EngagementPartnerUserId is { } ep) return ep;
        return financeUser.PartnerUserId;
    }

    /// <summary>Manager billable reviewer: project DM, else EP, else org manager. Partner → always self.</summary>
    public static Guid? ResolveManagerPartnerBillableApproverId(Project? project, AppUser submitter)
    {
        if (submitter.Role == AppRole.Partner)
            return submitter.Id;
        if (project?.DeliveryManagerUserId is { } dm) return dm;
        if (project?.EngagementPartnerUserId is { } ep) return ep;
        return submitter.ManagerUserId;
    }

    /// <summary>True when <paramref name="managerId"/> is the effective IC/Admin expense reviewer (project DM, else EP, else org manager).</summary>
    public static async Task<bool> ManagerMayApproveIcExpenseAsync(
        AppDbContext db,
        Guid managerId,
        ExpenseEntry expense,
        AppUser submitter,
        CancellationToken ct)
    {
        if (submitter.Role == AppRole.Finance)
            return false;
        if (!UsesDeliveryManagerWeekApproval(submitter.Role)) return false;
        var p = await FindActiveProjectByClientAndProjectName(db, expense.Client, expense.Project, ct);
        var effective = ResolveIcOrAdminBillableApproverId(p, submitter);
        return effective == managerId;
    }

    public static async Task<bool> ReviewerMayApproveFinanceExpenseAsync(
        AppDbContext db,
        Guid reviewerId,
        ExpenseEntry expense,
        AppUser submitter,
        CancellationToken ct)
    {
        if (submitter.Role != AppRole.Finance) return false;
        var p = await FindActiveProjectByClientAndProjectName(db, expense.Client, expense.Project, ct);
        var effective = ResolveFinanceBillableApproverId(p, submitter);
        return effective == reviewerId;
    }

    public static async Task<bool> PartnerMayApproveManagerExpenseAsync(
        AppDbContext db,
        Guid reviewerId,
        ExpenseEntry expense,
        AppUser submitter,
        CancellationToken ct)
    {
        if (!UsesEngagementPartnerWeekApproval(submitter.Role)) return false;
        var p = await FindActiveProjectByClientAndProjectName(db, expense.Client, expense.Project, ct);
        var effective = ResolveManagerPartnerBillableApproverId(p, submitter);
        return effective is { } id && id == reviewerId;
    }

    /// <summary>
    /// Effective approvers for IC / Admin weeks: each billable (client, project) uses project delivery manager if set,
    /// else project engagement partner, else org <see cref="AppUser.ManagerUserId"/>; all billable groups must resolve to the same reviewer.
    /// </summary>
    public static async Task<(HashSet<Guid> ApproverIds, string? Error)> ResolveIcWeekApproverIdsAsync(
        AppDbContext db,
        Guid icUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == icUserId, ct);
        if (user is null || !UsesDeliveryManagerWeekApproval(user.Role))
            return ([], "This account does not use the delivery-manager weekly sign-off path.");

        var weekEnd = weekStartMonday.AddDays(7);
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == icUserId && l.WorkDate >= weekStartMonday && l.WorkDate < weekEnd && l.IsBillable && l.Hours > 0)
            .Select(l => new { l.Client, l.Project })
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            if (user.ManagerUserId is { } m0) return ([m0], null);
            return ([], "Set an org manager on your profile, or add billable lines on projects with a delivery manager or engagement partner, before submitting this week.");
        }

        var approvers = new HashSet<Guid>();
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            var approver = ResolveIcOrAdminBillableApproverId(p, user);
            if (approver is { } id)
                approvers.Add(id);
            else
                return ([], "Billable lines include projects without a delivery manager, engagement partner, or org manager; set staffing on those projects or assign an org manager to your profile.");
        }

        if (approvers.Count > 1)
            return ([], "Billable hours this week span different approvers (project delivery managers / engagement partners / org manager); align to one approver before submitting.");
        return (approvers, null);
    }

    /// <summary>
    /// Finance weeks: each billable (client, project) uses project DM, else EP, else reporting partner; all must match.
    /// Weeks with no billable hours fall back to reporting partner only (must be active Partner).
    /// </summary>
    public static async Task<(HashSet<Guid> ApproverIds, string? Error)> ResolveFinanceWeekApproverIdsAsync(
        AppDbContext db,
        Guid financeUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == financeUserId, ct);
        if (user is null || user.Role != AppRole.Finance)
            return ([], "This account does not use the finance weekly sign-off path.");

        var weekEnd = weekStartMonday.AddDays(7);
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == financeUserId && l.WorkDate >= weekStartMonday && l.WorkDate < weekEnd && l.IsBillable && l.Hours > 0)
            .Select(l => new { l.Client, l.Project })
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            if (user.PartnerUserId is not { } pid0)
                return ([], "Finance accounts must have a reporting partner assigned, or add billable lines on staffed projects, before submitting this week.");
            var par0 = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                u => u.Id == pid0 && u.IsActive && u.Role == AppRole.Partner,
                ct);
            if (par0 is null)
                return ([], "Reporting partner is missing or inactive.");
            return ([pid0], null);
        }

        var approvers = new HashSet<Guid>();
        Guid? reportingPartnerIdUsed = null;
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            var approver = ResolveFinanceBillableApproverId(p, user);
            if (approver is not { } id)
                return ([], "Billable lines include projects without a delivery manager, engagement partner, or reporting partner on your profile; set staffing or assign a reporting partner.");
            if (id == user.PartnerUserId)
                reportingPartnerIdUsed = id;
            approvers.Add(id);
        }

        if (reportingPartnerIdUsed is { } rp)
        {
            var par = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                u => u.Id == rp && u.IsActive && u.Role == AppRole.Partner,
                ct);
            if (par is null)
                return ([], "Reporting partner is missing or inactive.");
        }

        if (approvers.Count > 1)
            return ([], "Billable hours this week span different approvers (project delivery managers / engagement partners / reporting partner); align to one approver before submitting.");
        return (approvers, null);
    }

    /// <summary>
    /// Manager / Partner weeks: each billable uses <see cref="ResolveManagerPartnerBillableApproverId"/>; Partner submitters always resolve to self.
    /// </summary>
    public static async Task<(HashSet<Guid> ApproverIds, string? Error)> ResolveManagerPartnerWeekApproverIdsAsync(
        AppDbContext db,
        Guid userId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !UsesEngagementPartnerWeekApproval(user.Role))
            return ([], "This account does not use the manager/partner weekly sign-off path.");

        var weekEnd = weekStartMonday.AddDays(7);
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == userId && l.WorkDate >= weekStartMonday && l.WorkDate < weekEnd && l.IsBillable && l.Hours > 0)
            .Select(l => new { l.Client, l.Project })
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            if (user.Role == AppRole.Partner)
                return ([user.Id], null);
            if (user.ManagerUserId is { } m0)
                return ([m0], null);
            return ([], "Set an org manager on your profile, or add billable lines on projects with a delivery manager or engagement partner, before submitting this week.");
        }

        var approvers = new HashSet<Guid>();
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            var approver = ResolveManagerPartnerBillableApproverId(p, user);
            if (approver is not { } id)
                return ([], "Billable lines include projects without a delivery manager, engagement partner, or org manager; set staffing on those projects or assign an org manager to your profile.");
            approvers.Add(id);
        }

        if (approvers.Count > 1)
            return ([], "Billable hours this week span different approvers (project delivery managers / engagement partners / org manager); align to one approver before submitting.");
        return (approvers, null);
    }

    public static async Task<bool> ManagerMayApproveIcTimesheetWeekAsync(
        AppDbContext db,
        Guid managerId,
        Guid icUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var (approvers, _) = await ResolveIcWeekApproverIdsAsync(db, icUserId, weekStartMonday, ct);
        return approvers.Count == 1 && approvers.Contains(managerId);
    }

    public static async Task<bool> PartnerMayApproveFinanceTimesheetWeekAsync(
        AppDbContext db,
        Guid reviewerId,
        Guid financeUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var (approvers, _) = await ResolveFinanceWeekApproverIdsAsync(db, financeUserId, weekStartMonday, ct);
        return approvers.Count == 1 && approvers.Contains(reviewerId);
    }

    public static async Task<bool> PartnerMayApproveManagerTimesheetWeekAsync(
        AppDbContext db,
        Guid reviewerId,
        Guid submitterUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var (approvers, _) = await ResolveManagerPartnerWeekApproverIdsAsync(db, submitterUserId, weekStartMonday, ct);
        return approvers.Count == 1 && approvers.Contains(reviewerId);
    }

    public static async Task<string?> ValidateIcWeekSubmitAsync(AppDbContext db, Guid icUserId, DateOnly weekStartMonday, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == icUserId, ct);
        if (u is null || !UsesDeliveryManagerWeekApproval(u.Role))
            return "This account does not use the delivery-manager weekly sign-off path.";

        var (_, err) = await ResolveIcWeekApproverIdsAsync(db, icUserId, weekStartMonday, ct);
        return err;
    }

    public static async Task<string?> ValidateFinanceWeekSubmitAsync(AppDbContext db, Guid financeUserId, DateOnly weekStartMonday, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == financeUserId, ct);
        if (u is null || u.Role != AppRole.Finance)
            return "Only Finance accounts use the finance weekly sign-off path.";
        var (_, err) = await ResolveFinanceWeekApproverIdsAsync(db, financeUserId, weekStartMonday, ct);
        return err;
    }

    public static async Task<string?> ValidateManagerWeekSubmitAsync(AppDbContext db, Guid managerUserId, DateOnly weekStartMonday, CancellationToken ct)
    {
        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerUserId, ct);
        if (mgr is null || !UsesEngagementPartnerWeekApproval(mgr.Role))
            return "Only Manager or Partner accounts use the manager/partner weekly sign-off path.";
        var (_, err) = await ResolveManagerPartnerWeekApproverIdsAsync(db, managerUserId, weekStartMonday, ct);
        return err;
    }
}
