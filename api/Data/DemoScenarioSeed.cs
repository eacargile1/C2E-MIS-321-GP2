using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// Idempotent Development seed: three packaged demo scenarios (clients prefixed with
/// <see cref="DemoScenarioMarkers.ClientNamePrefix"/>). Requires dev role users.
/// Enable with <c>Seed:DemoScenario</c>. Each scenario is skipped if its anchor client already exists.
/// </summary>
public static class DemoScenarioSeed
{
    /// <summary>First scenario anchor (legacy constant for callers/tests).</summary>
    public const string AnchorClientName = "DEMO SCENARIO — Contoso";

    private sealed record DemoScenarioDefinition(
        string ScenarioId,
        string AnchorClientName,
        string SecondaryClientName,
        string Project1Name,
        string Project2Name,
        decimal Rate1,
        decimal Rate2,
        string QuoteRef1,
        string QuoteRef2,
        string PtoReason,
        int PtoStartWeeksAhead,
        bool SeedIcWeekApprovalsAndHeavyTimesheets);

    private static readonly DemoScenarioDefinition[] Scenarios =
    [
        new DemoScenarioDefinition(
            ScenarioId: "s1",
            AnchorClientName: "DEMO SCENARIO — Contoso",
            SecondaryClientName: "DEMO SCENARIO — Fabrikam",
            Project1Name: "DSC-Portal-2026",
            Project2Name: "DSC-Analytics-Pilot",
            Rate1: 195m,
            Rate2: 215m,
            QuoteRef1: "DEMO-SCN1-PORTAL-01",
            QuoteRef2: "DEMO-SCN1-ANLY-01",
            PtoReason: $"Seeded PTO — Contoso/Fabrikam queue. {DemoScenarioMarkers.PtoReasonMarker}",
            PtoStartWeeksAhead: 2,
            SeedIcWeekApprovalsAndHeavyTimesheets: true),
        new DemoScenarioDefinition(
            ScenarioId: "s2",
            AnchorClientName: "DEMO SCENARIO — Ridge Logistics",
            SecondaryClientName: "DEMO SCENARIO — Summit Health",
            Project1Name: "DSC2-EDI-Rollout",
            Project2Name: "DSC2-CareNav-Mobile",
            Rate1: 188m,
            Rate2: 205m,
            QuoteRef1: "DEMO-SCN2-EDI-01",
            QuoteRef2: "DEMO-SCN2-CARE-01",
            PtoReason: $"Seeded PTO — Ridge/Summit queue. {DemoScenarioMarkers.PtoReasonMarker}",
            PtoStartWeeksAhead: 3,
            SeedIcWeekApprovalsAndHeavyTimesheets: false),
        new DemoScenarioDefinition(
            ScenarioId: "s3",
            AnchorClientName: "DEMO SCENARIO — Cobalt Manufacturing",
            SecondaryClientName: "DEMO SCENARIO — Polaris Media",
            Project1Name: "DSC3-ShopFloor-IoT",
            Project2Name: "DSC3-Streaming-Revamp",
            Rate1: 201m,
            Rate2: 198m,
            QuoteRef1: "DEMO-SCN3-IOT-01",
            QuoteRef2: "DEMO-SCN3-STREAM-01",
            PtoReason: $"Seeded PTO — Cobalt/Polaris queue. {DemoScenarioMarkers.PtoReasonMarker}",
            PtoStartWeeksAhead: 5,
            SeedIcWeekApprovalsAndHeavyTimesheets: false),
    ];

    public static async Task EnsureAsync(
        AppDbContext db,
        PasswordHasher<AppUser> _hasher,
        CancellationToken ct = default)
    {
        var ic = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevIcEmail, ct);
        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevManagerEmail, ct);
        var partner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevPartnerEmail, ct);
        var finance = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == DevRoleAccountsSeed.DevFinanceEmail, ct);
        var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == AppRole.Admin, ct)
            ?? await db.Users.AsNoTracking().FirstOrDefaultAsync(ct);

        if (ic is null || mgr is null || admin is null)
            return;

        foreach (var def in Scenarios)
            await SeedSingleScenarioAsync(db, def, ic.Id, mgr.Id, partner?.Id, finance?.Id, admin.Id, ct);
    }

    private static async Task SeedSingleScenarioAsync(
        AppDbContext db,
        DemoScenarioDefinition def,
        Guid icId,
        Guid mgrId,
        Guid? partnerId,
        Guid? financeId,
        Guid adminId,
        CancellationToken ct)
    {
        if (await db.Clients.AnyAsync(c => c.Name == def.AnchorClientName, ct))
            return;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var thisMonday = MondayOfWeek(today);
        var marker = DemoScenarioMarkers.ExpenseMarker(def.ScenarioId);

        var c1 = new Client
        {
            Id = Guid.NewGuid(),
            Name = def.AnchorClientName,
            ContactName = def.ScenarioId == "s1" ? "Alex Rivera" : def.ScenarioId == "s2" ? "Jordan Kim" : "Sam Ortiz",
            ContactEmail = $"contacts+{def.ScenarioId}@demo.c2e.local",
            ContactPhone = "555-0140",
            DefaultBillingRate = def.Rate1,
            Notes = "Seeded by DemoScenarioSeed (Development).",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var c2 = new Client
        {
            Id = Guid.NewGuid(),
            Name = def.SecondaryClientName,
            ContactName = def.ScenarioId == "s1" ? "Jamie Lee" : def.ScenarioId == "s2" ? "Riley Chen" : "Taylor Brooks",
            ContactEmail = $"contacts2+{def.ScenarioId}@demo.c2e.local",
            DefaultBillingRate = def.Rate2,
            Notes = "Seeded by DemoScenarioSeed (Development).",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Clients.AddRange(c1, c2);

        var p1 = new Project
        {
            Id = Guid.NewGuid(),
            Name = def.Project1Name,
            ClientId = c1.Id,
            BudgetAmount = def.ScenarioId == "s1" ? 380_000m : def.ScenarioId == "s2" ? 265_000m : 310_000m,
            IsActive = true,
            DeliveryManagerUserId = mgrId,
            EngagementPartnerUserId = partnerId,
            AssignedFinanceUserId = financeId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var p2 = new Project
        {
            Id = Guid.NewGuid(),
            Name = def.Project2Name,
            ClientId = c2.Id,
            BudgetAmount = def.ScenarioId == "s1" ? 112_000m : def.ScenarioId == "s2" ? 142_000m : 128_000m,
            IsActive = true,
            DeliveryManagerUserId = mgrId,
            EngagementPartnerUserId = partnerId,
            AssignedFinanceUserId = financeId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Projects.AddRange(p1, p2);

        db.ProjectTeamMembers.AddRange(
            new ProjectTeamMember { ProjectId = p1.Id, UserId = icId },
            new ProjectTeamMember { ProjectId = p1.Id, UserId = mgrId },
            new ProjectTeamMember { ProjectId = p2.Id, UserId = icId });

        db.ProjectEmployeeAssignments.AddRange(
            new ProjectEmployeeAssignment { ProjectId = p1.Id, UserId = icId, AssignedAtUtc = now.AddDays(-14) },
            new ProjectEmployeeAssignment { ProjectId = p2.Id, UserId = icId, AssignedAtUtc = now.AddDays(-10) });

        var prevMonday = thisMonday.AddDays(-7);
        if (def.SeedIcWeekApprovalsAndHeavyTimesheets)
        {
            await AddIcTimesheetWeekAsync(db, icId, c1.Name, p1.Name, prevMonday, prevMonday.AddDays(4), now, ct);
            var currentEnd = Min(today, thisMonday.AddDays(4));
            if (currentEnd >= thisMonday)
                await AddIcTimesheetWeekAsync(db, icId, c1.Name, p1.Name, thisMonday, currentEnd, now, ct);
        }
        else
        {
            await AddIcTimesheetWeekAsync(db, icId, c1.Name, p1.Name, prevMonday, prevMonday.AddDays(2), now, ct);
            if (today >= thisMonday)
                await AddIcTimesheetWeekAsync(db, icId, c1.Name, p1.Name, thisMonday, Min(today, thisMonday.AddDays(2)), now, ct);
        }

        var reviewed = now.AddDays(-2);
        var exp1 = $"Client workshop dinner {marker}";
        var exp2 = $"Regional travel bundle {marker}";
        var exp3 = $"Manager sync meal {marker}";

        if (!await db.ExpenseEntries.AnyAsync(e => e.UserId == icId && e.Description == exp1, ct))
        {
            db.ExpenseEntries.Add(new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icId,
                ExpenseDate = today.AddDays(-4),
                Client = c1.Name,
                Project = p1.Name,
                Category = "Meals",
                Description = exp1,
                Amount = 118.50m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        if (!await db.ExpenseEntries.AnyAsync(e => e.UserId == icId && e.Description == exp2, ct))
        {
            db.ExpenseEntries.Add(new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icId,
                ExpenseDate = today.AddDays(-11),
                Client = c2.Name,
                Project = p2.Name,
                Category = "Travel",
                Description = exp2,
                Amount = 412.80m,
                Status = ExpenseStatus.Approved,
                ReviewedByUserId = mgrId,
                ReviewedAtUtc = reviewed,
                CreatedAtUtc = now,
                UpdatedAtUtc = reviewed,
            });
        }

        if (!await db.ExpenseEntries.AnyAsync(e => e.UserId == mgrId && e.Description == exp3, ct))
        {
            db.ExpenseEntries.Add(new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = mgrId,
                ExpenseDate = today.AddDays(-3),
                Client = c2.Name,
                Project = p2.Name,
                Category = "Meals",
                Description = exp3,
                Amount = 96.00m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        if (!await db.ClientQuotes.AnyAsync(q => q.ReferenceNumber == def.QuoteRef1, ct))
        {
            db.ClientQuotes.Add(new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c1.Id,
                ReferenceNumber = def.QuoteRef1,
                Title = def.ScenarioId == "s1"
                    ? "Portal modernization — phase 1"
                    : def.ScenarioId == "s2"
                        ? "EDI rollout — integration slice"
                        : "Shop floor IoT — pilot",
                ScopeSummary = "Seeded scope for packaged demo.",
                EstimatedHours = def.ScenarioId == "s1" ? 880m : 520m,
                HourlyRate = def.Rate1,
                TotalAmount = Math.Round((def.ScenarioId == "s1" ? 880m : 520m) * def.Rate1, 2),
                Status = QuoteStatus.Sent,
                ValidThrough = today.AddMonths(2),
                CreatedByUserId = adminId,
                CreatedAtUtc = now.AddDays(-12),
                UpdatedAtUtc = now.AddDays(-12),
            });
        }

        if (!await db.ClientQuotes.AnyAsync(q => q.ReferenceNumber == def.QuoteRef2, ct))
        {
            db.ClientQuotes.Add(new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c2.Id,
                ReferenceNumber = def.QuoteRef2,
                Title = def.ScenarioId == "s1"
                    ? "Analytics pilot — fixed discovery"
                    : def.ScenarioId == "s2"
                        ? "Care navigation — mobile MVP"
                        : "Streaming platform revamp",
                ScopeSummary = "Seeded scope for packaged demo.",
                EstimatedHours = def.ScenarioId == "s1" ? 300m : 280m,
                HourlyRate = def.Rate2,
                TotalAmount = Math.Round((def.ScenarioId == "s1" ? 300m : 280m) * def.Rate2, 2),
                Status = QuoteStatus.Draft,
                ValidThrough = today.AddMonths(1),
                CreatedByUserId = adminId,
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now.AddDays(-4),
            });
        }

        db.ProjectTasks.AddRange(
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p1.Id,
                Title = def.ScenarioId == "s1" ? "SSO integration (OIDC)" : def.ScenarioId == "s2" ? "EDI map canonical orders" : "IoT gateway hardening",
                Description = "Seeded task for demo.",
                RequiredSkills = def.ScenarioId == "s1" ? "dotnet, oidc, security" : def.ScenarioId == "s2" ? "integration, edi" : "embedded, dotnet",
                DueDate = today.AddDays(21),
                AssignedUserId = icId,
                Status = ProjectTaskStatus.InProgress,
                CreatedByUserId = mgrId,
                CreatedAtUtc = now.AddDays(-8),
                UpdatedAtUtc = now,
            },
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p1.Id,
                Title = def.ScenarioId == "s1" ? "Performance baseline" : def.ScenarioId == "s2" ? "Carrier certification" : "MES connector reliability",
                RequiredSkills = "sql, observability",
                DueDate = today.AddDays(35),
                AssignedUserId = icId,
                Status = ProjectTaskStatus.Open,
                CreatedByUserId = mgrId,
                CreatedAtUtc = now.AddDays(-5),
                UpdatedAtUtc = now,
            },
            new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = p2.Id,
                Title = def.ScenarioId == "s1" ? "Warehouse semantic model v1" : def.ScenarioId == "s2" ? "Patient intake flows" : "CDN + DRM assessment",
                RequiredSkills = def.ScenarioId == "s1" ? "powerbi, sql" : "mobile, ux",
                DueDate = today.AddDays(14),
                Status = ProjectTaskStatus.Open,
                CreatedByUserId = mgrId,
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now,
            });

        if (def.SeedIcWeekApprovalsAndHeavyTimesheets)
        {
            if (!await db.TimesheetWeekApprovals.AnyAsync(
                    a => a.UserId == icId && a.WeekStartMonday == prevMonday,
                    ct))
            {
                db.TimesheetWeekApprovals.Add(new TimesheetWeekApproval
                {
                    Id = Guid.NewGuid(),
                    UserId = icId,
                    WeekStartMonday = prevMonday,
                    Status = TimesheetWeekApprovalStatus.Approved,
                    SubmittedAtUtc = now.AddDays(-10),
                    ReviewedByUserId = mgrId,
                    ReviewedAtUtc = now.AddDays(-9),
                });
            }

            if (!await db.TimesheetWeekApprovals.AnyAsync(
                    a => a.UserId == icId && a.WeekStartMonday == thisMonday,
                    ct))
            {
                db.TimesheetWeekApprovals.Add(new TimesheetWeekApproval
                {
                    Id = Guid.NewGuid(),
                    UserId = icId,
                    WeekStartMonday = thisMonday,
                    Status = TimesheetWeekApprovalStatus.Pending,
                    SubmittedAtUtc = now.AddHours(-6),
                });
            }
        }

        var ptoStart = thisMonday.AddDays(def.PtoStartWeeksAhead * 7);
        var ptoEnd = ptoStart.AddDays(2);
        if (!await db.PtoRequests.AnyAsync(p => p.UserId == icId && p.Reason == def.PtoReason, ct))
        {
            db.PtoRequests.Add(new PtoRequest
            {
                Id = Guid.NewGuid(),
                UserId = icId,
                StartDate = ptoStart,
                EndDate = ptoEnd,
                Reason = def.PtoReason,
                Status = PtoRequestStatus.Pending,
                ApproverUserId = mgrId,
                SecondaryApproverUserId = partnerId,
                CreatedAtUtc = now.AddDays(-1),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly MondayOfWeek(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;
        var daysSinceMonday = dow == 0 ? 6 : dow - 1;
        return date.AddDays(-daysSinceMonday);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static async Task AddIcTimesheetWeekAsync(
        AppDbContext db,
        Guid userId,
        string clientName,
        string projectName,
        DateOnly weekStart,
        DateOnly weekEndInclusive,
        DateTime nowUtc,
        CancellationToken ct)
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

            if (await db.TimesheetLines.AnyAsync(
                    t => t.UserId == userId &&
                         t.WorkDate == d &&
                         t.Client == clientName &&
                         t.Project == projectName &&
                         t.Task == task &&
                         !t.IsDeleted,
                    ct))
                continue;

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
