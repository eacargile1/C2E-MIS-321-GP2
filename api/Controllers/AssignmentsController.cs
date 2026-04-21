using C2E.Api;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/assignments")]
public sealed class AssignmentsController(AppDbContext db, IStaffingRecommendationService recommendationService) : ControllerBase
{
    [HttpGet("employees")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult<IReadOnlyList<AssignmentResponse>>> ListAssignableEmployees(CancellationToken ct)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Select(u => new AssignmentResponse
            {
                UserId = u.Id,
                Email = u.Email,
                DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
                    ? UserProfileName.DefaultFromEmail(u.Email)
                    : u.DisplayName,
                Role = u.Role.ToString(),
                ManagerUserId = u.ManagerUserId,
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("clients/{clientId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult<IReadOnlyList<AssignmentResponse>>> ListClientAssignments(Guid clientId, CancellationToken ct)
    {
        var clientExists = await db.Clients
            .AsNoTracking()
            .AnyAsync(c => c.Id == clientId && c.IsActive, ct);
        if (!clientExists)
            return NotFound(new AuthErrorResponse { Message = "Client not found or inactive." });

        var rows = await db.ClientEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .Join(
                db.Users.AsNoTracking().Where(u => u.IsActive),
                a => a.UserId,
                u => u.Id,
                (_, u) => new
                {
                    u.Id,
                    u.Email,
                    u.DisplayName,
                    u.Role,
                    u.ManagerUserId,
                })
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);

        return Ok(rows.Select(u => new AssignmentResponse
        {
            UserId = u.Id,
            Email = u.Email,
            DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
                ? UserProfileName.DefaultFromEmail(u.Email)
                : u.DisplayName,
            Role = u.Role.ToString(),
            ManagerUserId = u.ManagerUserId,
        }).ToList());
    }

    [HttpPut("clients/{clientId:guid}/employees/{userId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult> AssignEmployeeToClient(Guid clientId, Guid userId, CancellationToken ct)
    {
        var clientExists = await db.Clients.AnyAsync(c => c.Id == clientId && c.IsActive, ct);
        if (!clientExists)
            return NotFound(new AuthErrorResponse { Message = "Client not found or inactive." });

        var userExists = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct);
        if (!userExists)
            return BadRequest(new AuthErrorResponse { Message = "Employee must reference an active user." });

        var exists = await db.ClientEmployeeAssignments
            .AnyAsync(a => a.ClientId == clientId && a.UserId == userId, ct);
        if (exists)
            return NoContent();

        db.ClientEmployeeAssignments.Add(new()
        {
            ClientId = clientId,
            UserId = userId,
            AssignedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("clients/{clientId:guid}/employees/{userId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult> UnassignEmployeeFromClient(Guid clientId, Guid userId, CancellationToken ct)
    {
        var row = await db.ClientEmployeeAssignments
            .FirstOrDefaultAsync(a => a.ClientId == clientId && a.UserId == userId, ct);
        if (row is null)
            return NoContent();

        db.ClientEmployeeAssignments.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("projects/{projectId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult<IReadOnlyList<AssignmentResponse>>> ListProjectAssignments(Guid projectId, CancellationToken ct)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.IsActive, ct);
        if (!projectExists)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });

        var rows = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Join(
                db.Users.AsNoTracking().Where(u => u.IsActive),
                a => a.UserId,
                u => u.Id,
                (_, u) => new
                {
                    u.Id,
                    u.Email,
                    u.DisplayName,
                    u.Role,
                    u.ManagerUserId,
                })
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);

        return Ok(rows.Select(u => new AssignmentResponse
        {
            UserId = u.Id,
            Email = u.Email,
            DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
                ? UserProfileName.DefaultFromEmail(u.Email)
                : u.DisplayName,
            Role = u.Role.ToString(),
            ManagerUserId = u.ManagerUserId,
        }).ToList());
    }

    [HttpPut("projects/{projectId:guid}/employees/{userId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult> AssignEmployeeToProject(Guid projectId, Guid userId, CancellationToken ct)
    {
        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId && p.IsActive, ct);
        if (!projectExists)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });

        var userExists = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct);
        if (!userExists)
            return BadRequest(new AuthErrorResponse { Message = "Employee must reference an active user." });

        var exists = await db.ProjectEmployeeAssignments
            .AnyAsync(a => a.ProjectId == projectId && a.UserId == userId, ct);
        if (exists)
            return NoContent();

        db.ProjectEmployeeAssignments.Add(new()
        {
            ProjectId = projectId,
            UserId = userId,
            AssignedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("projects/{projectId:guid}/employees/{userId:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndPartner)]
    public async Task<ActionResult> UnassignEmployeeFromProject(Guid projectId, Guid userId, CancellationToken ct)
    {
        var row = await db.ProjectEmployeeAssignments
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == userId, ct);
        if (row is null)
            return NoContent();

        db.ProjectEmployeeAssignments.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("projects/{projectId:guid}/recommendations")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<StaffingRecommendationResponseDto>> RecommendProjectStaffing(
        Guid projectId,
        [FromBody] StaffingRecommendationRequestDto? req,
        CancellationToken ct)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.IsActive, ct);
        if (!projectExists)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });

        var response = await recommendationService.RecommendForProjectAsync(
            projectId,
            req?.RequiredSkills ?? [],
            ct);
        return Ok(response);
    }

    /// <summary>Admin only: set who a user reports to (org manager). Use User Management create/edit for the primary UX.</summary>
    [HttpPatch("users/{userId:guid}/org-manager")]
    [Authorize(Roles = RbacRoleSets.AdminOnly)]
    public async Task<ActionResult> SetOrgManager(Guid userId, [FromBody] SetOrgManagerRequest? body, CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new AuthErrorResponse { Message = "Request body is required." });

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (target is null)
            return NotFound(new AuthErrorResponse { Message = "User not found." });

        if (body.ManagerUserId is { } mid)
        {
            var err = await ValidateOrgManagerAsync(mid, userId, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            if (await WouldCreateManagerCycleAsync(userId, mid, ct))
                return BadRequest(new AuthErrorResponse { Message = "That manager assignment would create a cycle." });
            target.ManagerUserId = mid;
        }
        else
            target.ManagerUserId = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> ValidateOrgManagerAsync(Guid managerUserId, Guid subjectUserId, CancellationToken ct)
    {
        if (managerUserId == subjectUserId)
            return "A user cannot be their own manager.";

        var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerUserId, ct);
        if (mgr is null || !mgr.IsActive)
            return "Manager must reference an active user.";
        if (mgr.Role != AppRole.Manager)
            return "Org manager must be an active account with role Manager.";
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
}
