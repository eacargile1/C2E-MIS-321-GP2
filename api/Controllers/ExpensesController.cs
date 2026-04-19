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
[Route("api/expenses")]
[Authorize]
public sealed class ExpensesController(AppDbContext db) : ControllerBase
{
    /// <summary>Full expense register for finance ops (all users, all approval states).</summary>
    [HttpGet("ledger")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListLedger(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var rows = await db.ExpenseEntries
            .AsNoTracking()
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListMine(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var rows = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseResponse>> Create([FromBody] CreateExpenseRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseDateOnly(body.ExpenseDate, out var expenseDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid expenseDate. Use YYYY-MM-DD." });

        try
        {
            await ActiveCatalogValidation.EnsureActiveClientAndProjectAsync(
                db,
                body.Client.Trim(),
                body.Project.Trim(),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AuthErrorResponse { Message = ex.Message });
        }

        var now = DateTime.UtcNow;
        var entity = new ExpenseEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExpenseDate = expenseDate,
            Client = body.Client.Trim(),
            Project = body.Project.Trim(),
            Category = body.Category.Trim(),
            Description = body.Description.Trim(),
            Amount = body.Amount,
            Status = ExpenseStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ExpenseEntries.Add(entity);
        await db.SaveChangesAsync(ct);

        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";
        return Ok(new ExpenseResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserEmail = userEmail,
            ExpenseDate = entity.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Client = entity.Client,
            Project = entity.Project,
            Category = entity.Category,
            Description = entity.Description,
            Amount = entity.Amount,
            Status = entity.Status.ToString(),
            ReviewedByEmail = null,
            ReviewedAtUtc = null,
        });
    }

    [HttpGet("approvals/pending")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListPendingApprovals(CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var q = db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.Status == ExpenseStatus.Pending);

        if (User.IsInRole(nameof(AppRole.Manager)) && !User.IsInRole(nameof(AppRole.Admin)))
        {
            q = q.Where(x => db.Users.Any(u => u.Id == x.UserId && u.ManagerUserId == reviewerId));
        }

        var rows = await q
            .OrderBy(x => x.ExpenseDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<IActionResult> Approve([FromRoute] Guid id, CancellationToken ct) =>
        await Review(id, ExpenseStatus.Approved, ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<IActionResult> Reject([FromRoute] Guid id, CancellationToken ct) =>
        await Review(id, ExpenseStatus.Rejected, ct);

    private async Task<IActionResult> Review(Guid id, ExpenseStatus nextStatus, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var row = await db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (row.Status != ExpenseStatus.Pending)
            return BadRequest(new AuthErrorResponse { Message = "Only pending expenses can be reviewed." });

        if (User.IsInRole(nameof(AppRole.Manager)) && !User.IsInRole(nameof(AppRole.Admin)))
        {
            var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
            if (submitter?.ManagerUserId != userId)
                return Forbid();
        }

        row.Status = nextStatus;
        row.ReviewedByUserId = userId;
        row.ReviewedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ExpenseResponse Map(ExpenseEntry x, IReadOnlyDictionary<Guid, string> users) =>
        new()
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = users.TryGetValue(x.UserId, out var owner) ? owner : "",
            ExpenseDate = x.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Client = x.Client,
            Project = x.Project,
            Category = x.Category,
            Description = x.Description,
            Amount = x.Amount,
            Status = x.Status.ToString(),
            ReviewedByEmail = x.ReviewedByUserId is { } uid && users.TryGetValue(uid, out var rev) ? rev : null,
            ReviewedAtUtc = x.ReviewedAtUtc,
        };

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

    private static bool TryParseDateOnly(string input, out DateOnly date) =>
        DateOnly.TryParseExact(
            input.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

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
