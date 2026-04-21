using System.Globalization;
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
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var role = GetUserRole();

        var query = db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.TeamMembers)
            .AsQueryable();

        if (!includeInactive || role != AppRole.Admin)
            query = query.Where(p => p.IsActive);
        if (clientId is { } id)
            query = query.Where(p => p.ClientId == id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
            query = query.Where(p => p.AssignedFinanceUserId == userId);

        var rows = await query
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(rows.Select(Map).ToList());
    }

    /// <summary>Managers, partners, and finance users for project staffing pickers and read-only labels.</summary>
    [HttpGet("staffing-users")]
    [Authorize(Roles = RbacRoleSets.ProjectStaffingDirectoryReaders)]
    public async Task<ActionResult<IReadOnlyList<ProjectStaffingUserResponse>>> StaffingUsers(CancellationToken ct)
    {
        var rows = await db.Users.AsNoTracking()
            .Where(u =>
                u.IsActive &&
                (u.Role == AppRole.IC ||
                 u.Role == AppRole.Manager ||
                 u.Role == AppRole.Partner ||
                 u.Role == AppRole.Finance))
            .OrderBy(u => u.Email)
            .Select(u => new ProjectStaffingUserResponse
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Role = u.Role.ToString(),
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Get(Guid id, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var p = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.TeamMembers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!p.IsActive && GetUserRole() != AppRole.Admin) return NotFound();
        if (!CanViewProject(userId, GetUserRole(), p)) return NotFound();
        return Ok(Map(p));
    }

    /// <summary>Approved / pending / rejected expenses for this catalog project (not available to IC).</summary>
    [HttpGet("{id:guid}/expense-insights")]
    public async Task<ActionResult<ProjectExpenseInsightsResponse>> GetExpenseInsights(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var role = GetUserRole();
        if (role == AppRole.IC) return Forbid();

        var p = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || p.Client is null) return NotFound();
        if (!p.IsActive && role != AppRole.Admin) return NotFound();
        if (!CanViewExpenseInsights(userId, role, p)) return Forbid();

        var clientName = p.Client.Name;
        var projectName = p.Name;

        var expenses = await db.ExpenseEntries.AsNoTracking()
            .Join(db.Users.AsNoTracking(), e => e.UserId, u => u.Id, (e, u) => new { e, u.Email })
            .Where(x =>
                x.e.Client.ToLower() == clientName.ToLowerInvariant() &&
                x.e.Project.ToLower() == projectName.ToLowerInvariant())
            .OrderByDescending(x => x.e.ExpenseDate)
            .ThenByDescending(x => x.e.CreatedAtUtc)
            .ToListAsync(ct);

        var rows = expenses.Select(x => new ProjectExpenseRowResponse
        {
            Id = x.e.Id,
            UserId = x.e.UserId,
            SubmitterEmail = x.Email,
            ExpenseDate = x.e.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status = x.e.Status.ToString(),
            Amount = x.e.Amount,
            Category = x.e.Category,
            Description = x.e.Description,
        }).ToList();

        var pending = expenses.Where(x => x.e.Status == ExpenseStatus.Pending).ToList();
        var approved = expenses.Where(x => x.e.Status == ExpenseStatus.Approved).ToList();
        var rejected = expenses.Where(x => x.e.Status == ExpenseStatus.Rejected).ToList();

        return Ok(new ProjectExpenseInsightsResponse
        {
            ClientName = clientName,
            ProjectName = projectName,
            BudgetAmount = p.BudgetAmount,
            PendingCount = pending.Count,
            ApprovedCount = approved.Count,
            RejectedCount = rejected.Count,
            PendingAmount = pending.Sum(x => x.e.Amount),
            ApprovedAmount = approved.Sum(x => x.e.Amount),
            RejectedAmount = rejected.Sum(x => x.e.Amount),
            Expenses = rows,
        });
    }

    [HttpPost]
    [Authorize(Roles = RbacRoleSets.AdminPartner)]
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

        var engagementPartnerUserId = body.EngagementPartnerUserId;
        if (engagementPartnerUserId is null && TryGetUserId(out var creatorId) && GetUserRole() == AppRole.Partner)
            engagementPartnerUserId = creatorId;

        var staffErr = await ValidateProjectStaffUserIdsAsync(
            body.DeliveryManagerUserId,
            engagementPartnerUserId,
            body.AssignedFinanceUserId,
            ct);
        if (staffErr is not null)
            return BadRequest(new AuthErrorResponse { Message = staffErr });

        if (body.TeamMemberUserIds is { } createTeam)
        {
            var teamErr = await ValidateTeamMemberUserIdsAsync(createTeam, ct);
            if (teamErr is not null)
                return BadRequest(new AuthErrorResponse { Message = teamErr });
        }

        var now = DateTime.UtcNow;
        var entity = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            ClientId = body.ClientId,
            BudgetAmount = body.BudgetAmount,
            IsActive = true,
            DeliveryManagerUserId = body.DeliveryManagerUserId,
            EngagementPartnerUserId = engagementPartnerUserId,
            AssignedFinanceUserId = body.AssignedFinanceUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Projects.Add(entity);
        if (body.TeamMemberUserIds is { } teamIds)
        {
            foreach (var uid in teamIds.Distinct())
                entity.TeamMembers.Add(new ProjectTeamMember { UserId = uid });
        }

        await db.SaveChangesAsync(ct);

        entity.Client = client;
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, Map(entity));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = RbacRoleSets.AdminPartnerFinanceProjectPatch)]
    public async Task<ActionResult<ProjectResponse>> Patch(Guid id, [FromBody] PatchProjectRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        if (body is
            {
                Name: null,
                ClientId: null,
                BudgetAmount: null,
                IsActive: null,
                DeliveryManagerUserId: null,
                EngagementPartnerUserId: null,
                AssignedFinanceUserId: null,
                ClearDeliveryManager: false,
                ClearEngagementPartner: false,
                ClearAssignedFinance: false,
                TeamMemberUserIds: null,
            })
            return BadRequest(new AuthErrorResponse { Message = "Provide at least one field to update." });

        if (!TryGetUserId(out var userId)) return Unauthorized();
        var role = GetUserRole();

        var entity = await db.Projects.Include(p => p.Client).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();

        if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
        {
            if (entity.AssignedFinanceUserId != userId)
                return Forbid();
            if (!IsFinanceBudgetOnlyPatch(body))
                return BadRequest(new AuthErrorResponse
                {
                    Message = "Finance may only update budgetAmount on projects they are assigned to.",
                });
        }

        if (body.Name is { } nameRaw)
        {
            if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
                return BadRequest(new AuthErrorResponse { Message = "Finance cannot change the project name." });
            var name = nameRaw.Trim();
            if (name.Length == 0)
                return BadRequest(new AuthErrorResponse { Message = "Name cannot be empty." });
            entity.Name = name;
        }

        if (body.ClientId is { } clientId)
        {
            if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
                return BadRequest(new AuthErrorResponse { Message = "Finance cannot change the client." });
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.IsActive, ct);
            if (client is null)
                return BadRequest(new AuthErrorResponse { Message = "Client not found or inactive." });
            entity.ClientId = clientId;
            entity.Client = client;
        }

        if (body.BudgetAmount is { } budget)
            entity.BudgetAmount = budget;

        if (body.IsActive is { } isActive)
        {
            if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
                return BadRequest(new AuthErrorResponse { Message = "Finance cannot change active status." });
            entity.IsActive = isActive;
        }

        if (body.ClearDeliveryManager || body.DeliveryManagerUserId is not null ||
            body.ClearEngagementPartner || body.EngagementPartnerUserId is not null ||
            body.ClearAssignedFinance || body.AssignedFinanceUserId is not null ||
            body.TeamMemberUserIds is not null)
        {
            if (role == AppRole.Finance && !User.IsInRole(nameof(AppRole.Admin)))
                return BadRequest(new AuthErrorResponse { Message = "Finance cannot change staffing assignments." });
        }

        if (body.ClearDeliveryManager)
            entity.DeliveryManagerUserId = null;
        else if (body.DeliveryManagerUserId is { } dm)
        {
            var err = await ValidateProjectStaffUserIdsAsync(dm, null, null, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            entity.DeliveryManagerUserId = dm;
        }

        if (body.ClearEngagementPartner)
            entity.EngagementPartnerUserId = null;
        else if (body.EngagementPartnerUserId is { } ep)
        {
            var err = await ValidateProjectStaffUserIdsAsync(null, ep, null, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            entity.EngagementPartnerUserId = ep;
        }

        if (body.ClearAssignedFinance)
            entity.AssignedFinanceUserId = null;
        else if (body.AssignedFinanceUserId is { } fin)
        {
            var err = await ValidateProjectStaffUserIdsAsync(null, null, fin, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            entity.AssignedFinanceUserId = fin;
        }

        if (body.TeamMemberUserIds is not null)
        {
            var teamErr = await ValidateTeamMemberUserIdsAsync(body.TeamMemberUserIds, ct);
            if (teamErr is not null)
                return BadRequest(new AuthErrorResponse { Message = teamErr });
            var existing = await db.ProjectTeamMembers.Where(x => x.ProjectId == entity.Id).ToListAsync(ct);
            db.ProjectTeamMembers.RemoveRange(existing);
            foreach (var uid in body.TeamMemberUserIds.Distinct())
                db.ProjectTeamMembers.Add(new ProjectTeamMember { ProjectId = entity.Id, UserId = uid });
        }

        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var mapped = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.TeamMembers)
            .FirstAsync(x => x.Id == entity.Id, ct);
        return Ok(Map(mapped));
    }

    private static bool IsFinanceBudgetOnlyPatch(PatchProjectRequest body) =>
        body is
        {
            Name: null,
            ClientId: null,
            IsActive: null,
            DeliveryManagerUserId: null,
            EngagementPartnerUserId: null,
            AssignedFinanceUserId: null,
            BudgetAmount: not null,
            ClearDeliveryManager: false,
            ClearEngagementPartner: false,
            ClearAssignedFinance: false,
            TeamMemberUserIds: null,
        };

    private static bool CanViewProject(Guid userId, AppRole role, Project p)
    {
        if (role is AppRole.Admin or AppRole.Partner or AppRole.Manager or AppRole.IC) return true;
        if (role == AppRole.Finance) return p.AssignedFinanceUserId == userId;
        return false;
    }

    private static bool CanViewExpenseInsights(Guid userId, AppRole role, Project p)
    {
        if (role is AppRole.Admin or AppRole.Partner or AppRole.Manager) return true;
        if (role == AppRole.Finance) return p.AssignedFinanceUserId == userId;
        return false;
    }

    private async Task<string?> ValidateProjectStaffUserIdsAsync(
        Guid? deliveryManagerUserId,
        Guid? engagementPartnerUserId,
        Guid? assignedFinanceUserId,
        CancellationToken ct)
    {
        if (deliveryManagerUserId is { } dm)
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dm && x.IsActive, ct);
            if (u is null) return "Delivery manager user not found or inactive.";
            if (u.Role != AppRole.Manager) return "Delivery manager must be an active Manager account.";
        }

        if (engagementPartnerUserId is { } ep)
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ep && x.IsActive, ct);
            if (u is null) return "Engagement partner user not found or inactive.";
            if (u.Role != AppRole.Partner) return "Engagement partner must be an active Partner account.";
        }

        if (assignedFinanceUserId is { } finId)
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == finId && x.IsActive, ct);
            if (u is null) return "Assigned finance user not found or inactive.";
            if (u.Role != AppRole.Finance) return "Assigned finance contact must be an active Finance account.";
        }

        return null;
    }

    private async Task<string?> ValidateTeamMemberUserIdsAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count > 200)
            return "Too many team members.";
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
            return null;
        var found = await db.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (found.Count != distinct.Count)
            return "One or more team members are missing or inactive.";
        return null;
    }

    private static ProjectResponse Map(Project p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ClientId = p.ClientId,
        ClientName = p.Client?.Name ?? "",
        BudgetAmount = p.BudgetAmount,
        IsActive = p.IsActive,
        DeliveryManagerUserId = p.DeliveryManagerUserId,
        EngagementPartnerUserId = p.EngagementPartnerUserId,
        AssignedFinanceUserId = p.AssignedFinanceUserId,
        TeamMemberUserIds = p.TeamMembers.Count == 0
            ? Array.Empty<Guid>()
            : p.TeamMembers.OrderBy(t => t.UserId).Select(t => t.UserId).ToList(),
    };

    private AppRole GetUserRole()
    {
        var r = User.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<AppRole>(r, out var role) ? role : AppRole.IC;
    }

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
