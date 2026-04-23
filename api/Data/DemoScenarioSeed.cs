using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// Idempotent Development seed for repeatable demos: clients, staffed projects, IC timesheets,
/// week approvals (approved prior week + pending current week), expenses, quotes, tasks, PTO.
/// Requires dev role users from <see cref="DevRoleAccountsSeed"/> (<c>ic.dev</c>, <c>manager.dev</c>, etc.).
/// Enable with <c>Seed:DemoScenario</c>. Skips entirely if <see cref="AnchorClientName"/> already exists.
/// </summary>
public static class DemoScenarioSeed
{
    public const string AnchorClientName = "DEMO SCENARIO — Contoso";

    public static async Task EnsureAsync(
        AppDbContext db,
        PasswordHasher<AppUser> _hasher,
        CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(c => c.Name == AnchorClientName, ct))
            return;

        var ic = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevIcEmail, ct);
        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevManagerEmail, ct);
        var partner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevPartnerEmail, ct);
        var finance = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevFinanceEmail, ct);
        var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == AppRole.Admin, ct)
            ?? await db.Users.AsNoTracking().FirstOrDefaultAsync(ct);

        if (ic is null || mgr is null || admin is null)
            return;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var thisMonday = MondayOfWeek(today);

        var c1 = new Client
        {
            Id = Guid.NewGuid(),
            Name = AnchorClientName,
            ContactName = "Alex Rivera",
            ContactEmail = "alex@contoso-demo.example",
            ContactPhone = "555-0140",
            DefaultBillingRate = 195m,
            Notes = "Seeded by DemoScenarioSeed (Development).",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var c2 = new Client
        {
            Id = Guid.NewGuid(),
            Name = "DEMO SCENARIO — Fabrikam",
            ContactName = "Jamie Lee",
            ContactEmail = "jamie@fabrikam-demo.example",
            DefaultBillingRate = 215m,
            Notes = "Seeded by DemoScenarioSeed (Development).",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Clients.AddRange(c1, c2);

        var p1 = new Project
        {
            Id = Guid.NewGuid(),
            Name = "DSC-Portal-2026",
            ClientId = c1.Id,
            BudgetAmount = 380_000m,
            IsActive = true,
            DeliveryManagerUserId = mgr.Id,
            EngagementPartnerUserId = partner?.Id,
            AssignedFinanceUserId = finance?.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var p2 = new Project
        {
            Id = Guid.NewGuid(),
            Name = "DSC-Analytics-Pilot",
            ClientId = c2.Id,
            BudgetAmount = 112_000m,
            IsActive = true,
            DeliveryManagerUserId = mgr.Id,
            EngagementPartnerUserId = partner?.Id,
            AssignedFinanceUserId = finance?.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Projects.AddRange(p1, p2);

        db.ProjectTeamMembers.AddRange(
            new ProjectTeamMember { ProjectId = p1.Id, UserId = ic.Id },
            new ProjectTeamMember { ProjectId = p1.Id, UserId = mgr.Id },
            new ProjectTeamMember { ProjectId = p2.Id, UserId = ic.Id });

        db.ProjectEmployeeAssignments.AddRange(
            new ProjectEmployeeAssignment { ProjectId = p1.Id, UserId = ic.Id, AssignedAtUtc = now.AddDays(-14) },
            new ProjectEmployeeAssignment { ProjectId = p2.Id, UserId = ic.Id, AssignedAtUtc = now.AddDays(-10) });

        var prevMonday = thisMonday.AddDays(-7);
        AddIcTimesheetWeek(db, ic.Id, c1.Name, p1.Name, prevMonday, prevMonday.AddDays(4), now);
        var currentEnd = Min(today, thisMonday.AddDays(4));
        if (currentEnd >= thisMonday)
            AddIcTimesheetWeek(db, ic.Id, c1.Name, p1.Name, thisMonday, currentEnd, now);

        var reviewed = now.AddDays(-2);
        db.ExpenseEntries.AddRange(
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = ic.Id,
                ExpenseDate = today.AddDays(-4),
                Client = c1.Name,
                Project = p1.Name,
                Category = "Meals",
                Description = "Client workshop — working dinner",
                Amount = 118.50m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = ic.Id,
                ExpenseDate = today.AddDays(-11),
                Client = c2.Name,
                Project = p2.Name,
                Category = "Travel",
                Description = "Regional flight + ground transport",
                Amount = 412.80m,
                Status = ExpenseStatus.Approved,
                ReviewedByUserId = mgr.Id,
                ReviewedAtUtc = reviewed,
                CreatedAtUtc = now,
                UpdatedAtUtc = reviewed,
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = mgr.Id,
                ExpenseDate = today.AddDays(-3),
                Client = c2.Name,
                Project = p2.Name,
                Category = "Meals",
                Description = "Partner sync dinner (billable)",
                Amount = 96.00m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });

        db.ClientQuotes.AddRange(
            new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c1.Id,
                ReferenceNumber = "DEMO-SCN-PORTAL-01",
                Title = "Portal modernization — phase 1",
                ScopeSummary = "Discovery, architecture, MVP slice (auth + shell). Blended team.",
                EstimatedHours = 880m,
                HourlyRate = 195m,
                TotalAmount = Math.Round(880m * 195m, 2),
                Status = QuoteStatus.Sent,
                ValidThrough = today.AddMonths(2),
                CreatedByUserId = admin.Id,
                CreatedAtUtc = now.AddDays(-12),
                UpdatedAtUtc = now.AddDays(-12),
            },
            new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c2.Id,
                ReferenceNumber = "DEMO-SCN-ANLY-01",
                Title = "Analytics pilot — fixed discovery",
                ScopeSummary = "4-week pilot: curated datasets + exec dashboard.",
                EstimatedHours = 300m,
                HourlyRate = 215m,
                TotalAmount = Math.Round(300m * 215m, 2),
                Status = QuoteStatus.Draft,
                ValidThrough = today.AddMonths(1),
                CreatedByUserId = admin.Id,
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now.AddDays(-4),
            });

        db.ProjectTasks.AddRange(
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p1.Id,
                Title = "SSO integration (OIDC)",
                Description = "Wire enterprise IdP; document rollout.",
                RequiredSkills = "dotnet, oidc, security",
                DueDate = today.AddDays(21),
                AssignedUserId = ic.Id,
                Status = ProjectTaskStatus.InProgress,
                CreatedByUserId = mgr.Id,
                CreatedAtUtc = now.AddDays(-8),
                UpdatedAtUtc = now,
            },
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p1.Id,
                Title = "Performance baseline",
                RequiredSkills = "sql, observability",
                DueDate = today.AddDays(35),
                AssignedUserId = ic.Id,
                Status = ProjectTaskStatus.Open,
                CreatedByUserId = mgr.Id,
                CreatedAtUtc = now.AddDays(-5),
                UpdatedAtUtc = now,
            },
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p2.Id,
                Title = "Warehouse semantic model v1",
                RequiredSkills = "powerbi, sql",
                DueDate = today.AddDays(14),
                Status = ProjectTaskStatus.Open,
                CreatedByUserId = mgr.Id,
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now,
            });

        db.TimesheetWeekApprovals.AddRange(
            new TimesheetWeekApproval
            {
                Id = Guid.NewGuid(),
                UserId = ic.Id,
                WeekStartMonday = prevMonday,
                Status = TimesheetWeekApprovalStatus.Approved,
                SubmittedAtUtc = now.AddDays(-10),
                ReviewedByUserId = mgr.Id,
                ReviewedAtUtc = now.AddDays(-9),
            },
            new TimesheetWeekApproval
            {
                Id = Guid.NewGuid(),
                UserId = ic.Id,
                WeekStartMonday = thisMonday,
                Status = TimesheetWeekApprovalStatus.Pending,
                SubmittedAtUtc = now.AddHours(-6),
            });

        var ptoStart = thisMonday.AddDays(14);
        var ptoEnd = ptoStart.AddDays(2);
        db.PtoRequests.Add(new PtoRequest
        {
            Id = Guid.NewGuid(),
            UserId = ic.Id,
            StartDate = ptoStart,
            EndDate = ptoEnd,
            Reason = "Seeded PTO — manager/partner approval queue demo.",
            Status = PtoRequestStatus.Pending,
            ApproverUserId = mgr.Id,
            SecondaryApproverUserId = partner?.Id,
            CreatedAtUtc = now.AddDays(-1),
        });

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly MondayOfWeek(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;
        // ISO week: Monday = 0 .. Sunday = 6
        var daysSinceMonday = dow == 0 ? 6 : dow - 1;
        return date.AddDays(-daysSinceMonday);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static void AddIcTimesheetWeek(
        AppDbContext db,
        Guid userId,
        string clientName,
        string projectName,
        DateOnly weekStart,
        DateOnly weekEndInclusive,
        DateTime nowUtc)
    {
        var templates = new (string Task, decimal Hours)[]
        {
            ("Implementation", 6.5m),
            ("Code review", 1.5m),
            ("Design / docs", 2m),
            ("Client workshop", 3m),
            ("Implementation", 7m),
        };

        for (var d = weekStart; d <= weekEndInclusive; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var idx = (int)(d.DayNumber - weekStart.DayNumber);
            idx = Math.Clamp(idx, 0, templates.Length - 1);
            var (task, hours) = templates[idx];

            db.TimesheetLines.Add(new TimesheetLine
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorkDate = d,
                Client = clientName,
                Project = projectName,
                Task = task,
                Hours = hours,
                IsBillable = true,
                Notes = "DemoScenarioSeed",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                IsDeleted = false,
            });
        }
    }
}
