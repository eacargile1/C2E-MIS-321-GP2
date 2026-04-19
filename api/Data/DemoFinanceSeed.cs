using C2E.Api;
using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>Populates demo clients, projects, expenses, and quotes in local Development when enabled.</summary>
public static class DemoFinanceSeed
{
    public static async Task EnsureAsync(
        AppDbContext db,
        PasswordHasher<AppUser> hasher,
        CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(ct)) return;

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == AppRole.Admin, ct)
            ?? await db.Users.FirstAsync(ct);

        var icEmail = "demo.ic@c2e.local";
        if (!await db.Users.AnyAsync(u => u.Email == icEmail, ct))
        {
            var ic = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = icEmail,
                DisplayName = UserProfileName.DefaultFromEmail(icEmail),
                PasswordHash = "",
                Role = AppRole.IC,
                IsActive = true,
            };
            ic.PasswordHash = hasher.HashPassword(ic, "DemoIc!1");
            db.Users.Add(ic);
            await db.SaveChangesAsync(ct);
        }

        var icUser = await db.Users.FirstAsync(u => u.Email == icEmail, ct);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var c1 = new Client
        {
            Id = Guid.NewGuid(),
            Name = "Northwind Traders",
            ContactName = "Anna Anders",
            ContactEmail = "anna@northwind.example",
            ContactPhone = "555-0100",
            DefaultBillingRate = 185m,
            Notes = "Enterprise portal rebuild; reimbursable travel pre-approved Q2.",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var c2 = new Client
        {
            Id = Guid.NewGuid(),
            Name = "Fabrikam, Inc.",
            ContactName = "Bob Kelly",
            ContactEmail = "bkelly@fabrikam.example",
            DefaultBillingRate = 210m,
            Notes = "Staff aug — analytics workstream.",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Clients.AddRange(c1, c2);

        db.Projects.AddRange(
            new Project
            {
                Id = Guid.NewGuid(),
                Name = "NWT-Portal-2026",
                ClientId = c1.Id,
                BudgetAmount = 420_000m,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new Project
            {
                Id = Guid.NewGuid(),
                Name = "FAB-Analytics-Pilot",
                ClientId = c2.Id,
                BudgetAmount = 95_000m,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });

        var reviewed = now.AddDays(-2);
        db.ExpenseEntries.AddRange(
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icUser.Id,
                ExpenseDate = today.AddDays(-5),
                Client = c1.Name,
                Project = "NWT-Portal-2026",
                Category = "Meals",
                Description = "Working lunch — solution design",
                Amount = 86.42m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icUser.Id,
                ExpenseDate = today.AddDays(-4),
                Client = c1.Name,
                Project = "NWT-Portal-2026",
                Category = "Travel",
                Description = "Airport parking + rideshare",
                Amount = 124.00m,
                Status = ExpenseStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icUser.Id,
                ExpenseDate = today.AddDays(-12),
                Client = c2.Name,
                Project = "FAB-Analytics-Pilot",
                Category = "Software",
                Description = "Power BI Pro (1 month)",
                Amount = 15.00m,
                Status = ExpenseStatus.Approved,
                ReviewedByUserId = admin.Id,
                ReviewedAtUtc = reviewed,
                CreatedAtUtc = now,
                UpdatedAtUtc = reviewed,
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icUser.Id,
                ExpenseDate = today.AddDays(-20),
                Client = c1.Name,
                Project = "NWT-Portal-2026",
                Category = "Entertainment",
                Description = "Team event (not client-related)",
                Amount = 512.00m,
                Status = ExpenseStatus.Rejected,
                ReviewedByUserId = admin.Id,
                ReviewedAtUtc = reviewed.AddDays(-1),
                CreatedAtUtc = now,
                UpdatedAtUtc = reviewed.AddDays(-1),
            },
            new ExpenseEntry
            {
                Id = Guid.NewGuid(),
                UserId = icUser.Id,
                ExpenseDate = today.AddDays(-1),
                Client = c2.Name,
                Project = "FAB-Analytics-Pilot",
                Category = "Hardware",
                Description = "USB-C dock for war room",
                Amount = 239.99m,
                Status = ExpenseStatus.Approved,
                ReviewedByUserId = admin.Id,
                ReviewedAtUtc = now.AddHours(-3),
                CreatedAtUtc = now,
                UpdatedAtUtc = now.AddHours(-3),
            });

        db.ClientQuotes.AddRange(
            new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c1.Id,
                ReferenceNumber = $"{today:yyyyMM}-DEMO-01",
                Title = "Portal modernization — phase 1",
                ScopeSummary = "Discovery, architecture, and MVP slice for auth + shell. Assumes blended team.",
                EstimatedHours = 920m,
                HourlyRate = 185m,
                TotalAmount = Math.Round(920m * 185m, 2),
                Status = QuoteStatus.Sent,
                ValidThrough = today.AddMonths(2),
                CreatedByUserId = admin.Id,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-10),
            },
            new ClientQuote
            {
                Id = Guid.NewGuid(),
                ClientId = c2.Id,
                ReferenceNumber = $"{today:yyyyMM}-DEMO-02",
                Title = "Analytics pilot — fixed discovery",
                ScopeSummary = "4-week pilot: warehouse model + executive dashboard.",
                EstimatedHours = 320m,
                HourlyRate = 210m,
                TotalAmount = Math.Round(320m * 210m, 2),
                Status = QuoteStatus.Draft,
                ValidThrough = today.AddMonths(1),
                CreatedByUserId = admin.Id,
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now.AddDays(-3),
            });

        await db.SaveChangesAsync(ct);
    }
}
