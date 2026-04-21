using C2E.Api;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(AppRole.Admin))]
public class UsersController(AppDbContext db, PasswordHasher<AppUser> passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        var skillsByUser = await db.UserSkills
            .AsNoTracking()
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Skills = g.Select(x => x.SkillName).ToList() })
            .ToDictionaryAsync(x => x.UserId, x => x.Skills, ct);

        return Ok(users.Select(u => ToResponse(u, skillsByUser.GetValueOrDefault(u.Id) ?? [])).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        var skills = await db.UserSkills
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderBy(s => s.SkillName)
            .Select(s => s.SkillName)
            .ToListAsync(ct);
        return Ok(ToResponse(u, skills));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var normalized = body.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return Conflict(new AuthErrorResponse { Message = "A user with this email already exists." });

        var displayName = string.IsNullOrWhiteSpace(body.DisplayName)
            ? UserProfileName.DefaultFromEmail(normalized)
            : body.DisplayName.Trim();

        AppRole role = AppRole.IC;
        if (!string.IsNullOrWhiteSpace(body.Role))
        {
            var roleTrimmed = body.Role.Trim();
            if (!Enum.GetNames<AppRole>().Any(n => string.Equals(n, roleTrimmed, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new AuthErrorResponse
                {
                    Message = $"Invalid role. Use one of: {string.Join(", ", Enum.GetNames<AppRole>())}.",
                });
            role = Enum.Parse<AppRole>(roleTrimmed, ignoreCase: true);
        }

        if (role == AppRole.IC && body.ManagerUserId is null)
            return BadRequest(new AuthErrorResponse
            {
                Message = "IC accounts require org manager (managerUserId) referencing an active Manager.",
            });

        if (role == AppRole.Manager)
        {
            var anyExistingManager =
                await db.Users.AsNoTracking().AnyAsync(u => u.IsActive && u.Role == AppRole.Manager, ct);
            if (anyExistingManager && body.ManagerUserId is null)
                return BadRequest(new AuthErrorResponse
                {
                    Message =
                        "Manager accounts require org manager (managerUserId) referencing an active Manager when another Manager already exists.",
                });
            if (anyExistingManager && body.PartnerUserId is null)
                return BadRequest(new AuthErrorResponse
                {
                    Message =
                        "Manager accounts require reporting partner (partnerUserId) referencing an active Partner when another Manager already exists.",
                });
        }

        Guid? effectivePartnerUserId = body.PartnerUserId;
        if (role == AppRole.Finance)
        {
            effectivePartnerUserId ??= await ResolveDefaultReportingPartnerIdAsync(excludeUserId: null, ct);
            if (effectivePartnerUserId is null)
                return BadRequest(new AuthErrorResponse
                {
                    Message =
                        "Finance accounts need a reporting partner. Add at least one active Partner user, or pass partnerUserId explicitly.",
                });
        }

        if (body.ManagerUserId is { } midCreate)
        {
            var err = await ValidateOrgManagerTargetAsync(midCreate, subjectUserId: null, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
        }

        if (effectivePartnerUserId is { } pidVal)
        {
            var errP = await ValidateReportingPartnerTargetAsync(pidVal, subjectUserId: null, ct);
            if (errP is not null)
                return BadRequest(new AuthErrorResponse { Message = errP });
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = displayName,
            PasswordHash = "",
            Role = role,
            IsActive = true,
            ManagerUserId = body.ManagerUserId,
            PartnerUserId = role == AppRole.Finance ? effectivePartnerUserId : body.PartnerUserId,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, body.Password);

        var createInvariant = await ValidateAllUserReportingAsync(user, ct);
        if (createInvariant is not null)
            return BadRequest(new AuthErrorResponse { Message = createInvariant });

        db.Users.Add(user);
        foreach (var skill in NormalizeSkills(body.Skills))
        {
            db.UserSkills.Add(new UserSkill
            {
                UserId = user.Id,
                SkillName = skill,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        var createdSkills = await db.UserSkills
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .Select(s => s.SkillName)
            .ToListAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToResponse(user, createdSkills));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Patch(Guid id, [FromBody] UpdateUserRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (body.Email is null && body.Password is null && body.IsActive is null && body.Role is null &&
            body.DisplayName is null && body.AssignManager != true && body.AssignPartner != true && body.Skills is null)
        {
            return BadRequest(new AuthErrorResponse
            {
                    Message =
                        "Provide at least one of email, password, isActive, role, displayName, assignManager, assignPartner, or skills.",
            });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        AppRole? newRole = null;
        if (body.Role is not null)
        {
            var roleTrimmed = body.Role.Trim();
            if (roleTrimmed.Length == 0 ||
                !Enum.GetNames<AppRole>().Any(n => string.Equals(n, roleTrimmed, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new AuthErrorResponse
                {
                    Message = $"Invalid role. Use one of: {string.Join(", ", Enum.GetNames<AppRole>())}.",
                });
            newRole = Enum.Parse<AppRole>(roleTrimmed, ignoreCase: true);
        }

        if (body.IsActive == false && user.Role == AppRole.Admin)
        {
            var otherActiveAdmin = await db.Users.AnyAsync(
                u => u.Id != id && u.Role == AppRole.Admin && u.IsActive,
                ct);
            if (!otherActiveAdmin)
                return Conflict(new AuthErrorResponse { Message = "Cannot deactivate the last active administrator." });
        }

        if (newRole is { } nr && nr != AppRole.Admin && user.Role == AppRole.Admin && user.IsActive)
        {
            var otherActiveAdmin = await db.Users.AnyAsync(
                u => u.Id != id && u.Role == AppRole.Admin && u.IsActive,
                ct);
            if (!otherActiveAdmin)
                return Conflict(new AuthErrorResponse { Message = "Cannot demote the last active administrator." });
        }

        if (body.Email is not null)
        {
            var normalized = body.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == normalized && u.Id != id, ct))
                return Conflict(new AuthErrorResponse { Message = "A user with this email already exists." });
            user.Email = normalized;
        }

        if (body.Password is not null)
            user.PasswordHash = passwordHasher.HashPassword(user, body.Password);

        if (body.IsActive is not null)
            user.IsActive = body.IsActive.Value;

        if (newRole is { } r)
            user.Role = r;

        if (body.AssignManager == true)
        {
            if (body.ManagerUserId is { } mid)
            {
                var err = await ValidateOrgManagerTargetAsync(mid, user.Id, ct);
                if (err is not null)
                    return BadRequest(new AuthErrorResponse { Message = err });
                if (await WouldCreateManagerCycleAsync(user.Id, mid, ct))
                    return BadRequest(new AuthErrorResponse { Message = "That manager assignment would create a cycle." });
                user.ManagerUserId = mid;
            }
            else
            {
                if (user.Role is AppRole.IC or AppRole.Manager)
                    return BadRequest(new AuthErrorResponse
                    {
                        Message = "Cannot remove org manager from IC or Manager accounts.",
                    });
                user.ManagerUserId = null;
            }
        }

        if (body.AssignPartner == true)
        {
            if (body.ClearPartner)
                user.PartnerUserId = null;
            else if (body.PartnerUserId is { } pid)
            {
                var errP = await ValidateReportingPartnerTargetAsync(pid, user.Id, ct);
                if (errP is not null)
                    return BadRequest(new AuthErrorResponse { Message = errP });
                user.PartnerUserId = pid;
            }
            else
            {
                if (user.Role == AppRole.Finance)
                    return BadRequest(new AuthErrorResponse
                    {
                        Message = "Cannot remove reporting partner from Finance accounts.",
                    });
                var peers = await db.Users.AsNoTracking()
                    .AnyAsync(u => u.IsActive && u.Role == AppRole.Manager && u.Id != user.Id, ct);
                if (user.Role == AppRole.Manager && peers)
                    return BadRequest(new AuthErrorResponse
                    {
                        Message = "Cannot remove reporting partner from Manager accounts when another Manager exists.",
                    });
                user.PartnerUserId = null;
            }
        }

        if (body.DisplayName is not null)
        {
            var d = body.DisplayName.Trim();
            user.DisplayName = d.Length > 0 ? d : UserProfileName.DefaultFromEmail(user.Email);
        }

        if (body.Skills is not null)
        {
            var current = await db.UserSkills
                .Where(s => s.UserId == user.Id)
                .ToListAsync(ct);
            db.UserSkills.RemoveRange(current);
            foreach (var skill in NormalizeSkills(body.Skills))
            {
                db.UserSkills.Add(new UserSkill
                {
                    UserId = user.Id,
                    SkillName = skill,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
        }

        if (user.Role == AppRole.Finance && user.PartnerUserId is null)
        {
            var defPid = await ResolveDefaultReportingPartnerIdAsync(user.Id, ct);
            if (defPid is null)
                return BadRequest(new AuthErrorResponse
                {
                    Message =
                        "Finance accounts need an active Partner as reporting partner. Add a Partner user or set partnerUserId when changing role to Finance.",
                });
            user.PartnerUserId = defPid;
        }

        var invariantErr = await ValidateAllUserReportingAsync(user, ct);
        if (invariantErr is not null)
            return BadRequest(new AuthErrorResponse { Message = invariantErr });

        await db.SaveChangesAsync(ct);
        var savedSkills = await db.UserSkills
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.SkillName)
            .Select(s => s.SkillName)
            .ToListAsync(ct);
        return Ok(ToResponse(user, savedSkills));
    }

    /// <summary>First active Partner by email (deterministic default for new Finance users).</summary>
    private async Task<Guid?> ResolveDefaultReportingPartnerIdAsync(Guid? excludeUserId, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking().Where(u => u.IsActive && u.Role == AppRole.Partner);
        if (excludeUserId is { } ex)
            q = q.Where(u => u.Id != ex);
        var id = await q.OrderBy(u => u.Email).Select(u => u.Id).FirstOrDefaultAsync(ct);
        return id == default ? null : id;
    }

    /// <summary>Org manager must be an active <see cref="AppRole.Manager"/> (same rule as assignments org-manager).</summary>
    private async Task<string?> ValidateOrgManagerTargetAsync(Guid managerUserId, Guid? subjectUserId, CancellationToken ct)
    {
        if (subjectUserId is { } uid && managerUserId == uid)
            return "A user cannot be their own org manager.";

        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerUserId, ct);
        if (mgr is null || !mgr.IsActive)
            return "Org manager must reference an active user.";
        if (mgr.Role != AppRole.Manager)
            return "Org manager must be an active account with role Manager.";
        return null;
    }

    private async Task<string?> ValidateIcOrManagerHasOrgManagerAsync(AppUser user, CancellationToken ct)
    {
        if (user.Role == AppRole.IC)
        {
            if (user.ManagerUserId is null)
                return "IC accounts must have an org manager (active Manager) assigned.";
            return await ValidateOrgManagerTargetAsync(user.ManagerUserId.Value, user.Id, ct);
        }

        if (user.Role != AppRole.Manager)
            return null;

        if (user.ManagerUserId is { } mid)
            return await ValidateOrgManagerTargetAsync(mid, user.Id, ct);

        var otherActiveManagers = await db.Users.AsNoTracking()
            .AnyAsync(u => u.IsActive && u.Role == AppRole.Manager && u.Id != user.Id, ct);
        return otherActiveManagers
            ? "Manager accounts must have an org manager (active Manager) assigned when another Manager exists."
            : null;
    }

    private async Task<string?> ValidateFinanceAndManagerReportingPartnersAsync(AppUser user, CancellationToken ct)
    {
        if (user.Role == AppRole.Finance)
        {
            if (user.PartnerUserId is null)
                return "Finance accounts must have a reporting partner (active Partner) assigned.";
            return await ValidateReportingPartnerTargetAsync(user.PartnerUserId.Value, user.Id, ct);
        }

        if (user.Role != AppRole.Manager)
            return null;

        var peers = await db.Users.AsNoTracking()
            .AnyAsync(u => u.IsActive && u.Role == AppRole.Manager && u.Id != user.Id, ct);
        if (peers && user.PartnerUserId is null)
            return "Manager accounts must have a reporting partner when another Manager exists.";
        if (user.PartnerUserId is { } pid)
            return await ValidateReportingPartnerTargetAsync(pid, user.Id, ct);
        return null;
    }

    private async Task<string?> ValidateAllUserReportingAsync(AppUser user, CancellationToken ct)
    {
        var a = await ValidateIcOrManagerHasOrgManagerAsync(user, ct);
        if (a is not null) return a;
        return await ValidateFinanceAndManagerReportingPartnersAsync(user, ct);
    }

    private async Task<string?> ValidateReportingPartnerTargetAsync(Guid partnerUserId, Guid? subjectUserId, CancellationToken ct)
    {
        if (subjectUserId is { } uid && partnerUserId == uid)
            return "A user cannot be their own reporting partner.";
        var p = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == partnerUserId, ct);
        if (p is null || !p.IsActive || p.Role != AppRole.Partner)
            return "Reporting partner must be an active account with role Partner.";
        return null;
    }

    private async Task<bool> WouldCreateManagerCycleAsync(Guid userId, Guid newManagerId, CancellationToken ct)
    {
        var step = newManagerId;
        for (var i = 0; i < 32; i++)
        {
            if (step == userId) return true;
            var next = await db.Users.AsNoTracking()
                .Where(u => u.Id == step)
                .Select(u => u.ManagerUserId)
                .FirstOrDefaultAsync(ct);
            if (next is null) return false;
            step = next.Value;
        }

        return true;
    }

    private static UserResponse ToResponse(AppUser u, List<string> skills) => new()
    {
        Id = u.Id,
        Email = u.Email,
        DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
            ? UserProfileName.DefaultFromEmail(u.Email)
            : u.DisplayName,
        Role = u.Role.ToString(),
        IsActive = u.IsActive,
        ManagerUserId = u.ManagerUserId,
        PartnerUserId = u.PartnerUserId,
        Skills = skills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList(),
    };

    private static List<string> NormalizeSkills(List<string>? raw) =>
        (raw ?? [])
            .Select(s => s?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
}
