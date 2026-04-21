using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

/// <summary>
/// When the database has no users, seeds one account per core role for local demos (admin + finance + manager + partner + IC).
/// Admin email is <c>Seed:DevUserEmail</c>; finance/manager/ic use fixed dev addresses.
/// All share <c>Seed:DevUserPassword</c>. IC is assigned to the dev manager for approval flows.
/// </summary>
public static class DevRoleAccountsSeed
{
    public const string DevFinanceEmail = "finance.dev@c2e.local";
    public const string DevManagerEmail = "manager.dev@c2e.local";
    public const string DevIcEmail = "ic.dev@c2e.local";
    public const string DevPartnerEmail = "partner.dev@c2e.local";

    public static async Task SeedWhenEmptyAsync(
        AppDbContext db,
        PasswordHasher<AppUser> hasher,
        string primaryAdminEmail,
        string password,
        CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var adminEmail = primaryAdminEmail.Trim().ToLowerInvariant();
        var accounts = new (string email, string displayName, AppRole role)[]
        {
            (adminEmail, "Dev Admin", AppRole.Admin),
            (DevFinanceEmail, "Dev Finance", AppRole.Finance),
            (DevManagerEmail, "Dev Manager", AppRole.Manager),
            (DevPartnerEmail, "Dev Partner", AppRole.Partner),
            (DevIcEmail, "Dev IC", AppRole.IC),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (email, displayName, role) in accounts)
        {
            if (!seen.Add(email)) continue;
            var u = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                PasswordHash = "",
                Role = role,
                IsActive = true,
            };
            u.PasswordHash = hasher.HashPassword(u, password);
            db.Users.Add(u);
        }

        await db.SaveChangesAsync(ct);

        var partner = await db.Users.FirstOrDefaultAsync(u => u.Email == DevPartnerEmail, ct);
        var mgr = await db.Users.FirstOrDefaultAsync(u => u.Email == DevManagerEmail, ct);
        var finance = await db.Users.FirstOrDefaultAsync(u => u.Email == DevFinanceEmail, ct);
        var ic = await db.Users.FirstOrDefaultAsync(u => u.Email == DevIcEmail, ct);
        if (partner is not null)
        {
            if (finance is not null) finance.PartnerUserId = partner.Id;
            if (mgr is not null) mgr.PartnerUserId = partner.Id;
        }

        if (mgr is not null && ic is not null) ic.ManagerUserId = mgr.Id;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds finance/manager/ic dev accounts if absent (e.g. DB was seeded before multi-role seed existed).
    /// Only intended for local Development; gate with <c>Seed:EnsureDevRoleAccounts</c> and host env.
    /// </summary>
    public static async Task EnsureAdditionalDevRoleUsersAsync(
        AppDbContext db,
        PasswordHasher<AppUser> hasher,
        string password,
        CancellationToken ct = default)
    {
        var pwd = password;
        var extra = new (string Email, string DisplayName, AppRole Role)[]
        {
            (DevFinanceEmail, "Dev Finance", AppRole.Finance),
            (DevManagerEmail, "Dev Manager", AppRole.Manager),
            (DevPartnerEmail, "Dev Partner", AppRole.Partner),
            (DevIcEmail, "Dev IC", AppRole.IC),
        };

        foreach (var (email, displayName, role) in extra)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;
            var u = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                PasswordHash = "",
                Role = role,
                IsActive = true,
            };
            u.PasswordHash = hasher.HashPassword(u, pwd);
            db.Users.Add(u);
        }

        await db.SaveChangesAsync(ct);

        var partner = await db.Users.FirstOrDefaultAsync(u => u.Email == DevPartnerEmail, ct);
        var mgr = await db.Users.FirstOrDefaultAsync(u => u.Email == DevManagerEmail, ct);
        var finance = await db.Users.FirstOrDefaultAsync(u => u.Email == DevFinanceEmail, ct);
        var ic = await db.Users.FirstOrDefaultAsync(u => u.Email == DevIcEmail, ct);
        if (partner is not null)
        {
            if (finance is not null && finance.PartnerUserId is null) finance.PartnerUserId = partner.Id;
            if (mgr is not null && mgr.PartnerUserId is null) mgr.PartnerUserId = partner.Id;
        }

        if (mgr is not null && ic is not null && ic.ManagerUserId is null) ic.ManagerUserId = mgr.Id;

        await db.SaveChangesAsync(ct);
    }
}
