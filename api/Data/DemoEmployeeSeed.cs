using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// Adds packaged demo roster (ICs, managers, partner, finance) for staffing and directory demos.
/// Runs after <see cref="DemoScenarioSeed"/> when demo clients exist. Idempotent per email.
/// Logins share <c>Seed:DevUserPassword</c>. Emails use <see cref="DemoScenarioMarkers.DemoEmployeeEmailDomain"/>.
/// </summary>
public static class DemoEmployeeSeed
{
    public static async Task EnsureAsync(
        AppDbContext db,
        PasswordHasher<AppUser> hasher,
        string password,
        CancellationToken ct = default)
    {
        if (!await db.Clients.AnyAsync(c => c.Name.StartsWith(DemoScenarioMarkers.ClientNamePrefix), ct))
            return;

        var pwd = password;
        var now = DateTime.UtcNow;

        var partner = await EnsureUserAsync(
            db, hasher, $"dana.nguyen{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Dana Nguyen", AppRole.Partner, pwd, null, null, ct);

        var finance = await EnsureUserAsync(
            db, hasher, $"sam.rivera{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Sam Rivera", AppRole.Finance, pwd, null, partner.Id, ct);

        var mgrRuby = await EnsureUserAsync(
            db, hasher, $"ruby.chen{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Ruby Chen", AppRole.Manager, pwd, null, partner.Id, ct);

        var mgrOmar = await EnsureUserAsync(
            db, hasher, $"omar.hassan{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Omar Hassan", AppRole.Manager, pwd, null, partner.Id, ct);

        var icElena = await EnsureUserAsync(
            db, hasher, $"elena.park{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Elena Park", AppRole.IC, pwd, mgrRuby.Id, partner.Id, ct);

        var icMarcus = await EnsureUserAsync(
            db, hasher, $"marcus.james{DemoScenarioMarkers.DemoEmployeeEmailDomain}", "Marcus James", AppRole.IC, pwd, mgrOmar.Id, partner.Id, ct);

        // Ensure core .dev accounts also exist and are active for demo walkthroughs.
        var devPartner = await EnsureUserAsync(
            db, hasher, DevRoleAccountsSeed.DevPartnerEmail, "Dev Partner", AppRole.Partner, pwd, null, null, ct);
        var devFinance = await EnsureUserAsync(
            db, hasher, DevRoleAccountsSeed.DevFinanceEmail, "Dev Finance", AppRole.Finance, pwd, null, devPartner.Id, ct);
        var devManager = await EnsureUserAsync(
            db, hasher, DevRoleAccountsSeed.DevManagerEmail, "Dev Manager", AppRole.Manager, pwd, null, devPartner.Id, ct);
        var devIc = await EnsureUserAsync(
            db, hasher, DevRoleAccountsSeed.DevIcEmail, "Dev IC", AppRole.IC, pwd, devManager.Id, devPartner.Id, ct);

        await EnsureSkillsAsync(db, icElena.Id, ["dotnet", "sql", "react", "azure"], now, ct);
        await EnsureSkillsAsync(db, icMarcus.Id, ["python", "powerbi", "integration", "data modeling"], now, ct);
        await EnsureSkillsAsync(db, mgrRuby.Id, ["delivery", "stakeholder mgmt", "agile"], now, ct);
        await EnsureSkillsAsync(db, mgrOmar.Id, ["risk mgmt", "scrum", "architecture review"], now, ct);
        await EnsureSkillsAsync(db, partner.Id, ["account growth", "SOW", "executive sponsor"], now, ct);
        await EnsureSkillsAsync(db, finance.Id, ["revenue recognition", "billing ops", "forecasting"], now, ct);

        var demoClients = await db.Clients
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        foreach (var c in demoClients)
        {
            foreach (var uid in new[] { icElena.Id, icMarcus.Id, mgrRuby.Id, mgrOmar.Id, finance.Id, devIc.Id, devManager.Id, devPartner.Id, devFinance.Id })
            {
                if (!await db.ClientEmployeeAssignments.AnyAsync(a => a.ClientId == c.Id && a.UserId == uid, ct))
                    db.ClientEmployeeAssignments.Add(new ClientEmployeeAssignment { ClientId = c.Id, UserId = uid });
            }
        }

        var demoProjects = await db.Projects
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id })
            .ToListAsync(ct);

        foreach (var p in demoProjects)
        {
            foreach (var (uid, when) in new (Guid uid, DateTime when)[]
                     {
                         (icElena.Id, now.AddDays(-21)),
                         (icMarcus.Id, now.AddDays(-18)),
                         (mgrRuby.Id, now.AddDays(-30)),
                         (mgrOmar.Id, now.AddDays(-28)),
                         (devIc.Id, now.AddDays(-20)),
                         (devManager.Id, now.AddDays(-26)),
                         (devPartner.Id, now.AddDays(-33)),
                         (devFinance.Id, now.AddDays(-24)),
                     })
            {
                if (!await db.ProjectEmployeeAssignments.AnyAsync(a => a.ProjectId == p.Id && a.UserId == uid, ct))
                    db.ProjectEmployeeAssignments.Add(new ProjectEmployeeAssignment
                    {
                        ProjectId = p.Id,
                        UserId = uid,
                        AssignedAtUtc = when,
                    });
            }

            foreach (var uid in new[] { icElena.Id, icMarcus.Id, mgrRuby.Id, mgrOmar.Id, devIc.Id, devManager.Id, devPartner.Id, devFinance.Id })
            {
                if (!await db.ProjectTeamMembers.AnyAsync(t => t.ProjectId == p.Id && t.UserId == uid, ct))
                    db.ProjectTeamMembers.Add(new ProjectTeamMember { ProjectId = p.Id, UserId = uid });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<AppUser> EnsureUserAsync(
        AppDbContext db,
        PasswordHasher<AppUser> hasher,
        string email,
        string displayName,
        AppRole role,
        string password,
        Guid? managerUserId,
        Guid? partnerUserId,
        CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (existing is not null)
        {
            var changed = false;
            if (existing.Role != role)
            {
                existing.Role = role;
                changed = true;
            }

            if (!existing.IsActive)
            {
                existing.IsActive = true;
                changed = true;
            }

            var bounded = displayName.Length > 80 ? displayName[..80] : displayName;
            if (!string.Equals(existing.DisplayName, bounded, StringComparison.Ordinal))
            {
                existing.DisplayName = bounded;
                changed = true;
            }

            if (managerUserId is not null && existing.ManagerUserId != managerUserId)
            {
                existing.ManagerUserId = managerUserId;
                changed = true;
            }

            if (partnerUserId is not null && existing.PartnerUserId != partnerUserId)
            {
                existing.PartnerUserId = partnerUserId;
                changed = true;
            }

            if (changed)
                await db.SaveChangesAsync(ct);
            return existing;
        }

        var u = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = displayName.Length > 80 ? displayName[..80] : displayName,
            PasswordHash = "",
            Role = role,
            IsActive = true,
            ManagerUserId = managerUserId,
            PartnerUserId = partnerUserId,
        };
        u.PasswordHash = hasher.HashPassword(u, password);
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return u;
    }

    private static async Task EnsureSkillsAsync(
        AppDbContext db,
        Guid userId,
        IReadOnlyList<string> skills,
        DateTime nowUtc,
        CancellationToken ct)
    {
        foreach (var raw in skills)
        {
            var name = raw.Trim();
            if (name.Length == 0 || name.Length > 80) continue;
            if (await db.UserSkills.AnyAsync(s => s.UserId == userId && s.SkillName == name, ct)) continue;
            db.UserSkills.Add(new UserSkill { UserId = userId, SkillName = name, CreatedAtUtc = nowUtc });
        }
    }
}
