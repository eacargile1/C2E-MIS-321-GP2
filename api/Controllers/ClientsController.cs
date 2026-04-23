using System.Security.Claims;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/clients")]
[Authorize]
public sealed class ClientsController(AppDbContext db) : ControllerBase
{
    /// <summary>Default hourly billing rates per active client (Admin/Finance/Manager).</summary>
    [HttpGet("billing-rates")]
    [Authorize(Roles = RbacRoleSets.AdminFinanceManager)]
    public async Task<ActionResult<IReadOnlyList<ClientBillingRateItemDto>>> GetBillingRates(CancellationToken ct)
    {
        var rows = await db.Clients
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ClientBillingRateItemDto
            {
                ClientId = c.Id,
                ClientName = c.Name,
                DefaultHourlyRate = c.DefaultBillingRate,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (!User.IsInRole(nameof(AppRole.Admin)))
            includeInactive = false;

        var query = db.Clients
            .AsNoTracking()
            .Include(c => c.Projects)
            .AsQueryable();
        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(term));
        }

        var rows = await query
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var billing = CanViewBillingRates();
        HashSet<Guid>? financePortfolio = null;
        if (User.IsInRole(nameof(AppRole.Finance)) &&
            Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name),
                out var financeActorId))
        {
            financePortfolio = await FinancePortfolioClientIdsForUserAsync(financeActorId, ct);
        }

        return Ok(rows
            .Select(c => Map(
                c,
                billing,
                financePortfolio is null ? null : financePortfolio.Contains(c.Id)))
            .ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientResponse>> Get(Guid id, CancellationToken ct = default)
    {
        var c = await db.Clients
            .AsNoTracking()
            .Include(x => x.Projects)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound();
        if (!c.IsActive && !User.IsInRole(nameof(AppRole.Admin)))
            return NotFound();

        HashSet<Guid>? financePortfolio = null;
        if (User.IsInRole(nameof(AppRole.Finance)) &&
            Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name),
                out var financeActorId))
        {
            financePortfolio = await FinancePortfolioClientIdsForUserAsync(financeActorId, ct);
        }

        var inPortfolio = financePortfolio is null ? (bool?)null : financePortfolio.Contains(c.Id);
        return Ok(Map(c, CanViewBillingRates(), inPortfolio));
    }

    [HttpPost]
    [Authorize(Roles = RbacRoleSets.AdminPartnerFinance)]
    public async Task<ActionResult<ClientResponse>> Create([FromBody] CreateClientRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        var normalized = body.Name.Trim();
        if (normalized.Length == 0)
            return BadRequest(new AuthErrorResponse { Message = "Name is required." });

        if (User.IsInRole(nameof(AppRole.Partner)) && !body.FinanceLeadUserId.HasValue)
        {
            return BadRequest(new AuthErrorResponse
            {
                Message = "Partners must assign a finance lead (Finance role user) when creating a client.",
            });
        }

        if (body.FinanceLeadUserId is { } finLeadId)
        {
            var finUser = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == finLeadId && u.IsActive, ct);
            if (finUser is null)
                return BadRequest(new AuthErrorResponse { Message = "Finance lead must reference an active user." });
            if (finUser.Role != AppRole.Finance)
                return BadRequest(new AuthErrorResponse { Message = "Finance lead must be a user in the Finance role." });
        }

        var now = DateTime.UtcNow;
        var entity = new Client
        {
            Id = Guid.NewGuid(),
            Name = normalized,
            ContactName = string.IsNullOrWhiteSpace(body.ContactName) ? null : body.ContactName.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(body.ContactEmail) ? null : body.ContactEmail.Trim().ToLowerInvariant(),
            ContactPhone = string.IsNullOrWhiteSpace(body.ContactPhone) ? null : body.ContactPhone.Trim(),
            DefaultBillingRate = body.DefaultBillingRate,
            Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Clients.Add(entity);
        await db.SaveChangesAsync(ct);

        if (body.FinanceLeadUserId is { } rosterFinId)
        {
            var existsAssign = await db.ClientEmployeeAssignments
                .AnyAsync(a => a.ClientId == entity.Id && a.UserId == rosterFinId, ct);
            if (!existsAssign)
            {
                db.ClientEmployeeAssignments.Add(new ClientEmployeeAssignment
                {
                    ClientId = entity.Id,
                    UserId = rosterFinId,
                    AssignedAtUtc = now,
                });
                await db.SaveChangesAsync(ct);
            }
        }

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, Map(entity, CanViewBillingRates(), null));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminOnly)]
    public async Task<ActionResult<ClientResponse>> Patch(Guid id, [FromBody] PatchClientRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        if (body is { Name: null, ContactName: null, ContactEmail: null, ContactPhone: null, DefaultBillingRate: null, Notes: null, IsActive: null })
            return BadRequest(new AuthErrorResponse { Message = "Provide at least one field to update." });

        var entity = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return NotFound();

        var now = DateTime.UtcNow;
        if (body.Name is { } nameRaw)
        {
            var name = nameRaw.Trim();
            if (name.Length == 0)
                return BadRequest(new AuthErrorResponse { Message = "Name cannot be empty." });
            entity.Name = name;
        }

        if (body.ContactName is not null)
            entity.ContactName = string.IsNullOrWhiteSpace(body.ContactName) ? null : body.ContactName.Trim();
        if (body.ContactEmail is not null)
            entity.ContactEmail = string.IsNullOrWhiteSpace(body.ContactEmail) ? null : body.ContactEmail.Trim().ToLowerInvariant();
        if (body.ContactPhone is not null)
            entity.ContactPhone = string.IsNullOrWhiteSpace(body.ContactPhone) ? null : body.ContactPhone.Trim();
        if (body.DefaultBillingRate is not null)
            entity.DefaultBillingRate = body.DefaultBillingRate;
        if (body.Notes is not null)
            entity.Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim();
        if (body.IsActive is not null)
            entity.IsActive = body.IsActive.Value;

        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Ok(Map(entity, includeBilling: true, financePortfolioMember: null));
    }

    private bool CanViewBillingRates() =>
        User.IsInRole(nameof(AppRole.Admin)) ||
        User.IsInRole(nameof(AppRole.Finance)) ||
        User.IsInRole(nameof(AppRole.Manager));

    private static ClientResponse Map(Models.Client c, bool includeBilling, bool? financePortfolioMember = null) =>
        new()
        {
            Id = c.Id,
            Name = c.Name,
            ContactName = c.ContactName,
            ContactEmail = c.ContactEmail,
            ContactPhone = c.ContactPhone,
            DefaultBillingRate = includeBilling ? c.DefaultBillingRate : null,
            Notes = c.Notes,
            IsActive = c.IsActive,
            Projects = (c.Projects ?? Array.Empty<Project>())
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ClientProjectStubDto { Id = p.Id, Name = p.Name ?? "" })
                .ToList(),
            FinancePortfolioMember = financePortfolioMember,
        };

    private async Task<HashSet<Guid>> FinancePortfolioClientIdsForUserAsync(Guid financeUserId, CancellationToken ct)
    {
        var roster = await db.ClientEmployeeAssignments.AsNoTracking()
            .Where(a => a.UserId == financeUserId)
            .Select(a => a.ClientId)
            .ToListAsync(ct);
        var fromProjects = await db.Projects.AsNoTracking()
            .Where(p => p.IsActive && p.AssignedFinanceUserId == financeUserId)
            .Select(p => p.ClientId)
            .ToListAsync(ct);
        return roster.Concat(fromProjects).ToHashSet();
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
