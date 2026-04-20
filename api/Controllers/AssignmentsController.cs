using C2E.Api;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/assignments")]
[Authorize(Roles = RbacRoleSets.AdminAndPartner)]
public sealed class AssignmentsController(AppDbContext db) : ControllerBase
{
    [HttpGet("employees")]
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
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("clients/{clientId:guid}")]
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
        }).ToList());
    }

    [HttpPut("clients/{clientId:guid}/employees/{userId:guid}")]
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
        }).ToList());
    }

    [HttpPut("projects/{projectId:guid}/employees/{userId:guid}")]
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
}
