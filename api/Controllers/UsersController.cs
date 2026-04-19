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

        if (body.ManagerUserId is { } midCreate)
        {
            var err = await ValidateManagerAssignmentAsync(midCreate, userBeingEdited: null, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = displayName,
            PasswordHash = "",
            Role = AppRole.IC,
            IsActive = true,
            ManagerUserId = body.ManagerUserId,
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

        if (body.Email is null && body.Password is null && body.IsActive is null && body.Role is null &&
            body.DisplayName is null && body.AssignManager != true)
        {
            return BadRequest(new AuthErrorResponse
            {
                Message = "Provide at least one of email, password, isActive, role, displayName, or assignManager.",
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

        if (body.AssignManager == true)
        {
            if (body.ManagerUserId is { } mid)
            {
                var err = await ValidateManagerAssignmentAsync(mid, user.Id, ct);
                if (err is not null)
                    return BadRequest(new AuthErrorResponse { Message = err });
                if (await WouldCreateManagerCycleAsync(user.Id, mid, ct))
                    return BadRequest(new AuthErrorResponse { Message = "That manager assignment would create a cycle." });
                user.ManagerUserId = mid;
            }
            else
                user.ManagerUserId = null;
        }

        if (body.DisplayName is not null)
        {
            var d = body.DisplayName.Trim();
            user.DisplayName = d.Length > 0 ? d : UserProfileName.DefaultFromEmail(user.Email);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private async Task<string?> ValidateManagerAssignmentAsync(Guid managerId, Guid? userBeingEdited, CancellationToken ct)
    {
        if (userBeingEdited is { } uid && managerId == uid)
            return "A user cannot be their own manager.";

        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerId, ct);
        if (mgr is null || !mgr.IsActive)
            return "Manager must reference an active user.";

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

    private static UserResponse ToResponse(AppUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
            ? UserProfileName.DefaultFromEmail(u.Email)
            : u.DisplayName,
        Role = u.Role.ToString(),
        IsActive = u.IsActive,
        ManagerUserId = u.ManagerUserId,
    };
}
