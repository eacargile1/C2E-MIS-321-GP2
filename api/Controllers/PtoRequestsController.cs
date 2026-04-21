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
[Route("api/pto-requests")]
[Authorize]
public sealed class PtoRequestsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PtoRequestResponse>> Create([FromBody] CreatePtoRequestDto body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out var userId)) return Unauthorized();

        if (!DateOnly.TryParseExact(body.StartDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid startDate. Use YYYY-MM-DD." });
        if (!DateOnly.TryParseExact(body.EndDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return BadRequest(new AuthErrorResponse { Message = "Invalid endDate. Use YYYY-MM-DD." });
        if (end < start)
            return BadRequest(new AuthErrorResponse { Message = "endDate must be on or after startDate." });

        var requester = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (requester is null || !requester.IsActive)
            return Unauthorized();

        var res = await PtoRouting.ResolvePtoApproversAsync(db, requester, ct);
        if (res.Error is not null)
            return BadRequest(new AuthErrorResponse { Message = res.Error });
        if (!res.AutoApprove && res.PrimaryApproverUserId is null)
            return BadRequest(new AuthErrorResponse { Message = "Could not resolve approver." });

        var overlap = await db.PtoRequests.AnyAsync(
            p => p.UserId == userId &&
                 p.Status == PtoRequestStatus.Pending &&
                 !(p.EndDate < start || p.StartDate > end),
            ct);
        if (overlap)
            return BadRequest(new AuthErrorResponse { Message = "You already have a pending PTO request that overlaps these dates." });

        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "" : body.Reason.Trim();
        if (reason.Length > 2000)
            return BadRequest(new AuthErrorResponse { Message = "Reason is too long." });

        var entity = new PtoRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartDate = start,
            EndDate = end,
            Reason = reason,
            ApproverUserId = res.PrimaryApproverUserId!.Value,
            SecondaryApproverUserId = res.SecondaryApproverUserId,
            CreatedAtUtc = now,
        };

        if (res.AutoApprove)
        {
            entity.Status = PtoRequestStatus.Approved;
            entity.ReviewedByUserId = userId;
            entity.ReviewedAtUtc = now;
        }
        else
            entity.Status = PtoRequestStatus.Pending;

        db.PtoRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        var created = await LoadAsync(entity.Id, ct);
        return StatusCode(StatusCodes.Status201Created, Map(created!));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<PtoRequestResponse>>> GetMine(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var rows = await db.PtoRequests.AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Approver)
            .Include(p => p.SecondaryApprover)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(Map).ToList());
    }

    /// <summary>Approver queue: designated approver, or Admin sees all pending.</summary>
    [HttpGet("pending-approval")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<IReadOnlyList<PtoRequestResponse>>> ListPendingApproval(CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();
        var q = db.PtoRequests.AsNoTracking().Where(p => p.Status == PtoRequestStatus.Pending);
        if (!User.IsInRole(nameof(AppRole.Admin)))
            q = q.Where(p => p.ApproverUserId == reviewerId || p.SecondaryApproverUserId == reviewerId);

        var rows = await q
            .Include(p => p.User)
            .Include(p => p.Approver)
            .Include(p => p.SecondaryApprover)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(Map).ToList());
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();
        var row = await db.PtoRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return NotFound();
        if (row.Status != PtoRequestStatus.Pending)
            return BadRequest(new AuthErrorResponse { Message = "Only pending PTO requests can be approved." });

        if (!User.IsInRole(nameof(AppRole.Admin)) &&
            row.ApproverUserId != reviewerId &&
            row.SecondaryApproverUserId != reviewerId)
            return Forbid();

        row.Status = PtoRequestStatus.Approved;
        row.ReviewedByUserId = reviewerId;
        row.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();
        var row = await db.PtoRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return NotFound();
        if (row.Status != PtoRequestStatus.Pending)
            return BadRequest(new AuthErrorResponse { Message = "Only pending PTO requests can be rejected." });

        if (!User.IsInRole(nameof(AppRole.Admin)) &&
            row.ApproverUserId != reviewerId &&
            row.SecondaryApproverUserId != reviewerId)
            return Forbid();

        row.Status = PtoRequestStatus.Rejected;
        row.ReviewedByUserId = reviewerId;
        row.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<PtoRequest?> LoadAsync(Guid id, CancellationToken ct) =>
        await db.PtoRequests.AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Approver)
            .Include(p => p.SecondaryApprover)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    private static PtoRequestResponse Map(PtoRequest p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        UserEmail = p.User.Email,
        StartDate = p.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EndDate = p.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Reason = p.Reason,
        Status = p.Status.ToString(),
        ApproverUserId = p.ApproverUserId,
        ApproverEmail = p.Approver.Email,
        SecondaryApproverUserId = p.SecondaryApproverUserId,
        SecondaryApproverEmail = p.SecondaryApprover?.Email,
        CreatedAtUtc = p.CreatedAtUtc,
        ReviewedAtUtc = p.ReviewedAtUtc,
        ReviewedByUserId = p.ReviewedByUserId,
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
