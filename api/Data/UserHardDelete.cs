using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>Removes a user row and application data tied to that user (timesheets, expenses, PTO, assignments, etc.).</summary>
public static class UserHardDelete
{
    public static async Task DeleteAsync(AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var ptoToRemove = await db.PtoRequests
            .Where(p => p.UserId == userId || p.ApproverUserId == userId || p.SecondaryApproverUserId == userId)
            .ToListAsync(ct);
        db.PtoRequests.RemoveRange(ptoToRemove);

        var ptoReviewed = await db.PtoRequests.Where(p => p.ReviewedByUserId == userId).ToListAsync(ct);
        foreach (var p in ptoReviewed) p.ReviewedByUserId = null;

        var tasksCreated = await db.ProjectTasks.Where(t => t.CreatedByUserId == userId).ToListAsync(ct);
        db.ProjectTasks.RemoveRange(tasksCreated);

        var tasksAssigned = await db.ProjectTasks.Where(t => t.AssignedUserId == userId).ToListAsync(ct);
        foreach (var t in tasksAssigned) t.AssignedUserId = null;

        var lines = await db.TimesheetLines.Where(t => t.UserId == userId).ToListAsync(ct);
        db.TimesheetLines.RemoveRange(lines);

        var weekApprovals = await db.TimesheetWeekApprovals.Where(t => t.UserId == userId).ToListAsync(ct);
        db.TimesheetWeekApprovals.RemoveRange(weekApprovals);

        var weekReviewed = await db.TimesheetWeekApprovals.Where(t => t.ReviewedByUserId == userId).ToListAsync(ct);
        foreach (var w in weekReviewed) w.ReviewedByUserId = null;

        var expenses = await db.ExpenseEntries.Where(e => e.UserId == userId).ToListAsync(ct);
        db.ExpenseEntries.RemoveRange(expenses);

        var expenseReviewed = await db.ExpenseEntries.Where(e => e.ReviewedByUserId == userId).ToListAsync(ct);
        foreach (var e in expenseReviewed) e.ReviewedByUserId = null;

        var cea = await db.ClientEmployeeAssignments.Where(x => x.UserId == userId).ToListAsync(ct);
        db.ClientEmployeeAssignments.RemoveRange(cea);

        var pea = await db.ProjectEmployeeAssignments.Where(x => x.UserId == userId).ToListAsync(ct);
        db.ProjectEmployeeAssignments.RemoveRange(pea);

        var team = await db.ProjectTeamMembers.Where(x => x.UserId == userId).ToListAsync(ct);
        db.ProjectTeamMembers.RemoveRange(team);

        var skills = await db.UserSkills.Where(x => x.UserId == userId).ToListAsync(ct);
        db.UserSkills.RemoveRange(skills);

        var quotes = await db.ClientQuotes.Where(q => q.CreatedByUserId == userId).ToListAsync(ct);
        foreach (var q in quotes) q.CreatedByUserId = null;

        var projectsDm = await db.Projects.Where(p => p.DeliveryManagerUserId == userId).ToListAsync(ct);
        foreach (var p in projectsDm) p.DeliveryManagerUserId = null;
        var projectsEp = await db.Projects.Where(p => p.EngagementPartnerUserId == userId).ToListAsync(ct);
        foreach (var p in projectsEp) p.EngagementPartnerUserId = null;
        var projectsFin = await db.Projects.Where(p => p.AssignedFinanceUserId == userId).ToListAsync(ct);
        foreach (var p in projectsFin) p.AssignedFinanceUserId = null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
        }
    }
}
