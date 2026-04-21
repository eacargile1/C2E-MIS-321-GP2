using System.Globalization;
using System.Security.Claims;
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
[Route("api/project-tasks")]
[Authorize]
public sealed class ProjectTasksController(
    AppDbContext db,
    IStaffingRecommendationService recommendationService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<IReadOnlyList<ProjectTaskResponse>>> List(
        [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        var q = db.ProjectTasks
            .AsNoTracking()
            .Include(t => t.Project!)
            .ThenInclude(p => p.Client)
            .Include(t => t.AssignedUser)
            .Where(t => t.Project != null && t.Project.IsActive);

        if (projectId is { } pid)
            q = q.Where(t => t.ProjectId == pid);

        var rows = await q
            .OrderBy(t => t.Project!.Client!.Name)
            .ThenBy(t => t.Project.Name)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .ToListAsync(ct);

        return Ok(rows.Select(Map).ToList());
    }

    [HttpPost]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<ProjectTaskResponse>> Create([FromBody] CreateProjectTaskRequest body, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        var title = body.Title.Trim();
        if (title.Length == 0)
            return BadRequest(new AuthErrorResponse { Message = "Title is required." });

        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == body.ProjectId && p.IsActive, ct);
        if (project is null)
            return BadRequest(new AuthErrorResponse { Message = "Project not found or inactive." });

        var now = DateTime.UtcNow;
        DateOnly? due = null;
        if (!string.IsNullOrWhiteSpace(body.DueDate))
        {
            if (!DateOnly.TryParseExact(body.DueDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return BadRequest(new AuthErrorResponse { Message = "Invalid dueDate. Use YYYY-MM-DD." });
            due = d;
        }

        if (body.AssignedUserId is { } aid)
        {
            var okUser = await db.Users.AnyAsync(u => u.Id == aid && u.IsActive, ct);
            if (!okUser)
                return BadRequest(new AuthErrorResponse { Message = "Assigned user not found or inactive." });
        }

        var entity = new ProjectTask
        {
            Id = Guid.NewGuid(),
            ProjectId = body.ProjectId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            RequiredSkills = SerializeSkills(body.RequiredSkills),
            DueDate = due,
            AssignedUserId = body.AssignedUserId,
            Status = ProjectTaskStatus.Open,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ProjectTasks.Add(entity);
        await db.SaveChangesAsync(ct);

        var loaded = await LoadTaskAsync(entity.Id, ct);
        return loaded is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : StatusCode(StatusCodes.Status201Created, Map(loaded));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<ProjectTaskResponse>> Patch(Guid id, [FromBody] PatchProjectTaskRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        var entity = await db.ProjectTasks
            .Include(t => t.Project!)
            .ThenInclude(p => p.Client)
            .Include(t => t.AssignedUser)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null || entity.Project is null || !entity.Project.IsActive)
            return NotFound();

        if (body is
            {
                Title: null,
                Description: null,
                RequiredSkills: null,
                DueDate: null,
                AssignedUserId: null,
                ClearAssignedUser: false,
                Status: null,
            })
            return BadRequest(new AuthErrorResponse { Message = "Provide at least one field to update." });

        var now = DateTime.UtcNow;
        if (body.Title is { } titleRaw)
        {
            var t = titleRaw.Trim();
            if (t.Length == 0)
                return BadRequest(new AuthErrorResponse { Message = "Title cannot be empty." });
            entity.Title = t;
        }

        if (body.Description is not null)
            entity.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();

        if (body.RequiredSkills is not null)
            entity.RequiredSkills = SerializeSkills(body.RequiredSkills);

        if (body.DueDate is not null)
        {
            if (string.IsNullOrWhiteSpace(body.DueDate))
                entity.DueDate = null;
            else if (DateOnly.TryParseExact(body.DueDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                entity.DueDate = d;
            else
                return BadRequest(new AuthErrorResponse { Message = "Invalid dueDate. Use YYYY-MM-DD." });
        }

        if (body.ClearAssignedUser)
            entity.AssignedUserId = null;
        else if (body.AssignedUserId is { } aid)
        {
            var okUser = await db.Users.AnyAsync(u => u.Id == aid && u.IsActive, ct);
            if (!okUser)
                return BadRequest(new AuthErrorResponse { Message = "Assigned user not found or inactive." });
            entity.AssignedUserId = aid;
        }

        if (body.Status is { } stRaw)
        {
            if (!Enum.TryParse<ProjectTaskStatus>(stRaw.Trim(), ignoreCase: true, out var st))
                return BadRequest(new AuthErrorResponse { Message = "Invalid status." });
            entity.Status = st;
        }

        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        var loaded = await LoadTaskAsync(id, ct);
        return loaded is null ? NotFound() : Ok(Map(loaded));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
            return NoContent();
        db.ProjectTasks.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Staffing recommendations for this task’s project using the task’s required skills.</summary>
    [HttpPost("{id:guid}/recommendations")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<StaffingRecommendationResponseDto>> Recommend(Guid id, CancellationToken ct)
    {
        var task = await db.ProjectTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
            return NotFound(new AuthErrorResponse { Message = "Task not found." });

        var projectOk = await db.Projects.AnyAsync(p => p.Id == task.ProjectId && p.IsActive, ct);
        if (!projectOk)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });

        var skills = DeserializeSkills(task.RequiredSkills);
        return Ok(await recommendationService.RecommendForProjectAsync(task.ProjectId, skills, ct));
    }

    private async Task<ProjectTask?> LoadTaskAsync(Guid id, CancellationToken ct) =>
        await db.ProjectTasks
            .AsNoTracking()
            .Include(t => t.Project!)
            .ThenInclude(p => p.Client)
            .Include(t => t.AssignedUser)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    private static ProjectTaskResponse Map(ProjectTask t)
    {
        var clientName = t.Project?.Client?.Name ?? "";
        var projectName = t.Project?.Name ?? "";
        return new ProjectTaskResponse
        {
            Id = t.Id,
            ProjectId = t.ProjectId,
            ClientName = clientName,
            ProjectName = projectName,
            Title = t.Title,
            Description = t.Description,
            RequiredSkills = DeserializeSkills(t.RequiredSkills),
            DueDate = t.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AssignedUserId = t.AssignedUserId,
            AssignedEmail = t.AssignedUser?.Email,
            Status = t.Status.ToString(),
            CreatedByUserId = t.CreatedByUserId,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc,
        };
    }

    private static string SerializeSkills(IReadOnlyList<string>? skills) =>
        string.Join(
            ", ",
            (skills ?? [])
                .Select(s => s?.Trim().ToLowerInvariant() ?? "")
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40));

    private static List<string> DeserializeSkills(string stored) =>
        stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

    private bool TryGetUserId(out Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name);
        if (sub is null || !Guid.TryParse(sub, out id))
        {
            id = default;
            return false;
        }

        return true;
    }

    private string? FirstModelError()
    {
        foreach (var (_, state) in ModelState)
        {
            if (state.Errors.Count == 0) continue;
            var msg = state.Errors[0].ErrorMessage;
            if (!string.IsNullOrWhiteSpace(msg)) return msg;
        }

        return null;
    }
}
