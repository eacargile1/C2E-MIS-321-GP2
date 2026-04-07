using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> List(
        [FromQuery] Guid? clientId,
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(p => p.IsActive);
        if (clientId is { } id)
            query = query.Where(p => p.ClientId == id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        var rows = await query
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(rows.Select(Map).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Get(Guid id, CancellationToken ct = default)
    {
        var p = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!p.IsActive) return NotFound();
        return Ok(Map(p));
    }

    [HttpPost]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == body.ClientId && c.IsActive, ct);
        if (client is null)
            return BadRequest(new AuthErrorResponse { Message = "Client not found or inactive." });

        var name = body.Name.Trim();
        if (name.Length == 0)
            return BadRequest(new AuthErrorResponse { Message = "Name is required." });

        var now = DateTime.UtcNow;
        var entity = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            ClientId = body.ClientId,
            BudgetAmount = body.BudgetAmount,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Projects.Add(entity);
        await db.SaveChangesAsync(ct);

        entity.Client = client;
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, Map(entity));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<ProjectResponse>> Patch(Guid id, [FromBody] PatchProjectRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        if (body is { Name: null, ClientId: null, BudgetAmount: null, IsActive: null })
            return BadRequest(new AuthErrorResponse { Message = "Provide at least one field to update." });

        var entity = await db.Projects.Include(p => p.Client).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();

        if (body.Name is { } nameRaw)
        {
            var name = nameRaw.Trim();
            if (name.Length == 0)
                return BadRequest(new AuthErrorResponse { Message = "Name cannot be empty." });
            entity.Name = name;
        }

        if (body.ClientId is { } clientId)
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.IsActive, ct);
            if (client is null)
                return BadRequest(new AuthErrorResponse { Message = "Client not found or inactive." });
            entity.ClientId = clientId;
            entity.Client = client;
        }

        if (body.BudgetAmount is { } budget)
            entity.BudgetAmount = budget;
        if (body.IsActive is { } isActive)
            entity.IsActive = isActive;

        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Map(entity));
    }

    private static ProjectResponse Map(Project p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ClientId = p.ClientId,
        ClientName = p.Client?.Name ?? "",
        BudgetAmount = p.BudgetAmount,
        IsActive = p.IsActive,
    };

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
