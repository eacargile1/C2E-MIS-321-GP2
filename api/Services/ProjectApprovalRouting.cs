using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Services;

/// <summary>
/// Tiered approvals: IC / Finance / Admin → project <see cref="Project.DeliveryManagerUserId"/> (else org manager);
/// Manager / Partner → project <see cref="Project.EngagementPartnerUserId"/>.
/// </summary>
public static class ProjectApprovalRouting
{
    /// <summary>Roles whose weekly billable sign-off is delivery manager (or org manager fallback).</summary>
    public static bool UsesDeliveryManagerWeekApproval(AppRole role) =>
        role is AppRole.IC or AppRole.Finance or AppRole.Admin;

    /// <summary>Roles whose weekly billable sign-off is engagement partner on booked projects.</summary>
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

    public static async Task<bool> ManagerMayApproveIcExpenseAsync(
        AppDbContext db,
        Guid managerId,
        ExpenseEntry expense,
        AppUser submitter,
        CancellationToken ct)
    {
        if (!UsesDeliveryManagerWeekApproval(submitter.Role)) return false;
        var p = await FindActiveProjectByClientAndProjectName(db, expense.Client, expense.Project, ct);
        if (p?.DeliveryManagerUserId is { } dm)
            return dm == managerId;
        return submitter.ManagerUserId == managerId;
    }

    public static async Task<bool> PartnerMayApproveManagerExpenseAsync(
        AppDbContext db,
        Guid partnerId,
        ExpenseEntry expense,
        AppUser submitter,
        CancellationToken ct)
    {
        if (!UsesEngagementPartnerWeekApproval(submitter.Role)) return false;
        var p = await FindActiveProjectByClientAndProjectName(db, expense.Client, expense.Project, ct);
        return p?.EngagementPartnerUserId is { } ep && ep == partnerId;
    }

    /// <summary>
    /// Effective approvers for IC / Finance / Admin weeks: each billable (client, project) maps to project delivery manager if set, else org <see cref="AppUser.ManagerUserId"/> (must be same across the week).
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
            return ([], "Set an org manager on your profile, or add billable lines on projects with a delivery manager, before submitting this week.");
        }

        var approvers = new HashSet<Guid>();
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            if (p?.DeliveryManagerUserId is { } dm)
                approvers.Add(dm);
            else if (user.ManagerUserId is { } fallback)
                approvers.Add(fallback);
            else
                return ([], "Billable lines include projects without a delivery manager; set delivery managers on those projects or assign an org manager to your profile.");
        }

        if (approvers.Count > 1)
            return ([], "Billable hours this week span different approvers (delivery managers / org manager); align to one approver before submitting.");
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

    public static async Task<bool> PartnerMayApproveManagerTimesheetWeekAsync(
        AppDbContext db,
        Guid partnerId,
        Guid managerUserId,
        DateOnly weekStartMonday,
        CancellationToken ct)
    {
        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerUserId, ct);
        if (mgr is null || !UsesEngagementPartnerWeekApproval(mgr.Role)) return false;

        var weekEnd = weekStartMonday.AddDays(7);
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == managerUserId && l.WorkDate >= weekStartMonday && l.WorkDate < weekEnd && l.IsBillable && l.Hours > 0)
            .Select(l => new { l.Client, l.Project })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return false;

        var partnerIds = new HashSet<Guid>();
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            if (p?.EngagementPartnerUserId is { } ep)
                partnerIds.Add(ep);
        }

        return partnerIds.Count == 1 && partnerIds.Contains(partnerId);
    }

    public static async Task<string?> ValidateIcWeekSubmitAsync(AppDbContext db, Guid icUserId, DateOnly weekStartMonday, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == icUserId, ct);
        if (u is null || !UsesDeliveryManagerWeekApproval(u.Role))
            return "This account does not use the delivery-manager weekly sign-off path.";

        var (_, err) = await ResolveIcWeekApproverIdsAsync(db, icUserId, weekStartMonday, ct);
        return err;
    }

    public static async Task<string?> ValidateManagerWeekSubmitAsync(AppDbContext db, Guid managerUserId, DateOnly weekStartMonday, CancellationToken ct)
    {
        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerUserId, ct);
        if (mgr is null || !UsesEngagementPartnerWeekApproval(mgr.Role))
            return "Only Manager or Partner accounts use the engagement-partner weekly sign-off path.";

        var weekEnd = weekStartMonday.AddDays(7);
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == managerUserId && l.WorkDate >= weekStartMonday && l.WorkDate < weekEnd && l.IsBillable && l.Hours > 0)
            .Select(l => new { l.Client, l.Project })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return "Add at least one billable line on projects that name an engagement partner before submitting this week.";

        var partnerIds = new HashSet<Guid>();
        foreach (var ln in lines.DistinctBy(x => (x.Client, x.Project)))
        {
            var p = await FindActiveProjectByClientAndProjectName(db, ln.Client, ln.Project, ct);
            if (p?.EngagementPartnerUserId is { } ep)
                partnerIds.Add(ep);
        }

        if (partnerIds.Count == 0)
            return "Billable lines must be on projects with an engagement partner assigned for partner sign-off.";
        if (partnerIds.Count > 1)
            return "Billable hours this week span projects with different engagement partners; align to one partner before submitting.";
        return null;
    }
}
