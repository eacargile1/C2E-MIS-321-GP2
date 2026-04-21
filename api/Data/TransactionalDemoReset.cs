using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// Removes all catalog and time/expense data and any users that are not the standard dev seed accounts.
/// Intended for local Development demos only; call from <see cref="Controllers.AdminDevResetController"/>.
/// </summary>
public static class TransactionalDemoReset
{
    public static async Task ClearAsync(AppDbContext db, string primaryAdminEmail, CancellationToken ct = default)
    {
        var adminNorm = primaryAdminEmail.Trim().ToLowerInvariant();
        var keepEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            adminNorm,
            DevRoleAccountsSeed.DevFinanceEmail,
            DevRoleAccountsSeed.DevManagerEmail,
            DevRoleAccountsSeed.DevPartnerEmail,
            DevRoleAccountsSeed.DevIcEmail,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Users.ExecuteUpdateAsync(
            s => s.SetProperty(u => u.ManagerUserId, (Guid?)null).SetProperty(u => u.PartnerUserId, (Guid?)null),
            ct);

        await db.PtoRequests.ExecuteDeleteAsync(ct);
        await db.ProjectTasks.ExecuteDeleteAsync(ct);
        await db.ProjectTeamMembers.ExecuteDeleteAsync(ct);
        await db.ProjectEmployeeAssignments.ExecuteDeleteAsync(ct);
        await db.ClientEmployeeAssignments.ExecuteDeleteAsync(ct);
        await db.TimesheetWeekApprovals.ExecuteDeleteAsync(ct);
        await db.TimesheetLines.ExecuteDeleteAsync(ct);
        await db.ExpenseEntries.ExecuteDeleteAsync(ct);
        await db.ClientQuotes.ExecuteDeleteAsync(ct);
        await db.Projects.ExecuteDeleteAsync(ct);
        await db.Clients.ExecuteDeleteAsync(ct);
        await db.UserSkills.ExecuteDeleteAsync(ct);

        await db.Users.Where(u => !keepEmails.Contains(u.Email)).ExecuteDeleteAsync(ct);

        await RewireDevRoleLinksAsync(db, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task RewireDevRoleLinksAsync(AppDbContext db, CancellationToken ct)
    {
        var partner = await db.Users.FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevPartnerEmail, ct);
        var mgr = await db.Users.FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevManagerEmail, ct);
        var finance = await db.Users.FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevFinanceEmail, ct);
        var ic = await db.Users.FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevIcEmail, ct);
        if (partner is not null)
        {
            if (finance is not null) finance.PartnerUserId = partner.Id;
            if (mgr is not null) mgr.PartnerUserId = partner.Id;
        }

        if (mgr is not null && ic is not null) ic.ManagerUserId = mgr.Id;
    }
}
