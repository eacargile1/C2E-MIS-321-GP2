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
        return Ok(users.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        return Ok(ToResponse(u));
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
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = displayName,
            PasswordHash = "",
            Role = AppRole.IC,
            IsActive = true,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, body.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToResponse(user));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Patch(Guid id, [FromBody] UpdateUserRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (body.Email is null && body.Password is null && body.IsActive is null && body.Role is null && body.DisplayName is null)
            return BadRequest(new AuthErrorResponse { Message = "Provide at least one of email, password, isActive, role, or displayName." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        AppRole? newRole = null;
        if (body.Role is not null)
        {
            var roleTrimmed = body.Role.Trim();
            if (roleTrimmed.Length == 0 ||
                !Enum.GetNames<AppRole>().Any(n => string.Equals(n, roleTrimmed, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new AuthErrorResponse { Message = "Invalid role. Use IC, Admin, Manager, or Finance." });
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

        if (body.DisplayName is not null)
        {
            var d = body.DisplayName.Trim();
            user.DisplayName = d.Length > 0 ? d : UserProfileName.DefaultFromEmail(user.Email);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private static UserResponse ToResponse(AppUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
            ? UserProfileName.DefaultFromEmail(u.Email)
            : u.DisplayName,
        Role = u.Role.ToString(),
        IsActive = u.IsActive,
    };
}
