using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// Development-only: removes clients, projects, timesheets, expenses, invoices, etc. that are not part of
/// packaged demo data (clients named <see cref="DemoScenarioMarkers.ClientNamePrefix"/>*) and removes users
/// except the primary dev admin (<c>Seed:DevUserEmail</c>) plus fixed dev role accounts.
/// </summary>
public static class DevelopmentDataPurge
{
    public static async Task PurgeNonDemoDataAsync(
        AppDbContext db,
        string primaryDevAdminEmail,
        CancellationToken ct = default)
    {
        var protectedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            primaryDevAdminEmail.Trim(),
            DevRoleAccountsSeed.DevFinanceEmail,
            DevRoleAccountsSeed.DevManagerEmail,
            DevRoleAccountsSeed.DevPartnerEmail,
            DevRoleAccountsSeed.DevIcEmail,
        };

        var keepUserIds = await db.Users
            .AsNoTracking()
            .Where(u =>
                protectedEmails.Contains(u.Email) ||
                u.Email.EndsWith(DemoScenarioMarkers.DemoEmployeeEmailDomain, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (keepUserIds.Count == 0)
            throw new InvalidOperationException(
                "Purge aborted: no users matched Seed:DevUserEmail and dev role accounts. Check configuration.");

        var keepClientIds = await db.Clients
            .AsNoTracking()
            .Where(c => c.Name.StartsWith(DemoScenarioMarkers.ClientNamePrefix))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var keepProjectIds = await db.Projects
            .AsNoTracking()
            .Where(p => keepClientIds.Contains(p.ClientId))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var demoClientNames = await db.Clients
            .AsNoTracking()
            .Where(c => keepClientIds.Contains(c.Id))
            .Select(c => c.Name)
            .ToListAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Invoices + lines (lines cascade when deleting invoices in DB)
        if (keepProjectIds.Count == 0)
            await db.IssuedInvoices.ExecuteDeleteAsync(ct);
        else
            await db.IssuedInvoices.Where(i => !keepProjectIds.Contains(i.ProjectId)).ExecuteDeleteAsync(ct);

        if (keepProjectIds.Count == 0)
        {
            await db.ProjectTasks.ExecuteDeleteAsync(ct);
            await db.ProjectTeamMembers.ExecuteDeleteAsync(ct);
            await db.ProjectEmployeeAssignments.ExecuteDeleteAsync(ct);
            await db.Projects.ExecuteDeleteAsync(ct);
        }
        else
        {
            await db.ProjectTasks.Where(t => !keepProjectIds.Contains(t.ProjectId)).ExecuteDeleteAsync(ct);
            await db.ProjectTeamMembers.Where(m => !keepProjectIds.Contains(m.ProjectId)).ExecuteDeleteAsync(ct);
            await db.ProjectEmployeeAssignments.Where(a => !keepProjectIds.Contains(a.ProjectId)).ExecuteDeleteAsync(ct);
            await db.Projects.Where(p => !keepClientIds.Contains(p.ClientId)).ExecuteDeleteAsync(ct);
        }

        if (keepClientIds.Count == 0)
        {
            await db.ClientEmployeeAssignments.ExecuteDeleteAsync(ct);
            await db.ClientQuotes.ExecuteDeleteAsync(ct);
            await db.Clients.ExecuteDeleteAsync(ct);
        }
        else
        {
            await db.ClientEmployeeAssignments.Where(a => !keepClientIds.Contains(a.ClientId)).ExecuteDeleteAsync(ct);
            await db.ClientQuotes.Where(q => !keepClientIds.Contains(q.ClientId)).ExecuteDeleteAsync(ct);
            await db.Clients.Where(c => !keepClientIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }

        await db.TimesheetWeekApprovals.Where(a => !keepUserIds.Contains(a.UserId)).ExecuteDeleteAsync(ct);

        if (demoClientNames.Count == 0)
            await db.TimesheetLines.ExecuteDeleteAsync(ct);
        else
        {
            await db.TimesheetLines.Where(t => !keepUserIds.Contains(t.UserId)).ExecuteDeleteAsync(ct);
            await db.TimesheetLines
                .Where(t => keepUserIds.Contains(t.UserId) && !demoClientNames.Contains(t.Client))
                .ExecuteDeleteAsync(ct);
        }

        await db.ExpenseEntries.Where(e => !keepUserIds.Contains(e.UserId)).ExecuteDeleteAsync(ct);
        if (demoClientNames.Count == 0)
            await db.ExpenseEntries.Where(e => keepUserIds.Contains(e.UserId)).ExecuteDeleteAsync(ct);
        else
        {
            await db.ExpenseEntries
                .Where(e => keepUserIds.Contains(e.UserId) && !demoClientNames.Contains(e.Client))
                .ExecuteDeleteAsync(ct);
        }

        await db.PtoRequests.Where(p => !keepUserIds.Contains(p.UserId)).ExecuteDeleteAsync(ct);
        await db.PtoRequests
            .Where(p =>
                keepUserIds.Contains(p.UserId) &&
                !p.Reason.Contains(DemoScenarioMarkers.PtoReasonMarker))
            .ExecuteDeleteAsync(ct);

        await db.UserSkills.Where(s => !keepUserIds.Contains(s.UserId)).ExecuteDeleteAsync(ct);

        await db.Users.Where(u => !keepUserIds.Contains(u.Id)).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }
}
