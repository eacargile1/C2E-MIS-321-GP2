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

/// <summary>
/// Pre-submit / approver AI + deterministic checks for expenses and timesheets (human-in-the-loop; no auto-write).
/// </summary>
[ApiController]
[Route("api/ai/operations")]
[Authorize]
public sealed class OperationsAiController(IOperationsAiAdvisor advisor, AppDbContext db) : ControllerBase
{
    [HttpPost("expense-review")]
    public async Task<ActionResult<OperationsExpenseAiReviewResponse>> ExpenseReview(
        [FromBody] OperationsExpenseAiReviewRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out _))
            return Unauthorized();

        if (!TryParseDateOnly(body.ExpenseDate, out _))
            return BadRequest(new AuthErrorResponse { Message = "Invalid expenseDate. Use YYYY-MM-DD." });

        return Ok(await advisor.ReviewExpenseDraftAsync(body, ct));
    }

    [HttpPost("expense-approver-review")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<OperationsExpenseAiReviewResponse>> ExpenseApproverReview(
        [FromBody] OperationsExpenseApproverReviewRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out var reviewerId))
            return Unauthorized();
        if (body.ExpenseId == Guid.Empty)
            return BadRequest(new AuthErrorResponse { Message = "expenseId is required." });

        var row = await db.ExpenseEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == body.ExpenseId, ct);
        if (row is null) return NotFound();
        if (row.Status != ExpenseStatus.Pending)
            return BadRequest(new AuthErrorResponse { Message = "Only pending expenses can be reviewer-checked." });

        if (!User.IsInRole(nameof(AppRole.Admin)))
        {
            var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
            if (submitter is null) return Forbid();

            var ok = false;
            if (User.IsInRole(nameof(AppRole.Partner)))
            {
                if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
            }

            if (!ok && User.IsInRole(nameof(AppRole.Manager)))
            {
                if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, reviewerId, row, submitter, ct))
                    ok = true;
            }

            if (!ok) return Forbid();
        }

        var submitterEmail = await db.Users.AsNoTracking()
            .Where(u => u.Id == row.UserId)
            .Select(u => u.Email)
            .FirstAsync(ct);

        var req = new OperationsExpenseAiReviewRequest
        {
            ExpenseDate = row.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Client = row.Client,
            Project = row.Project,
            Category = row.Category,
            Description = row.Description,
            Amount = row.Amount,
            HasInvoiceAttachment = row.InvoiceBytes is { Length: > 0 },
        };

        return Ok(await advisor.ReviewExpenseDraftAsync(req, ct, approverContext: true, submitterEmail));
    }

    [HttpPost("timesheet-week-review")]
    public async Task<ActionResult<OperationsTimesheetWeekAiReviewResponse>> TimesheetWeekReview(
        [FromBody] OperationsTimesheetWeekAiReviewRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out _))
            return Unauthorized();

        if (!TryParseDateOnly(body.WeekStartMonday, out var mon))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStartMonday. Use YYYY-MM-DD." });
        if (mon.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStartMonday must be a Monday." });

        foreach (var line in body.Lines)
        {
            if (!TryParseDateOnly(line.WorkDate, out var wd))
                return BadRequest(new AuthErrorResponse { Message = "Invalid workDate on a line. Use YYYY-MM-DD." });
            if (wd < mon || wd >= mon.AddDays(7))
                return BadRequest(new AuthErrorResponse { Message = "Each workDate must fall within the week." });
        }

        return Ok(await advisor.ReviewTimesheetWeekAsync(body, ct));
    }

    [HttpPost("timesheet-approver-review")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<OperationsTimesheetWeekAiReviewResponse>> TimesheetApproverReview(
        [FromBody] OperationsTimesheetApproverReviewRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out var reviewerId))
            return Unauthorized();

        if (!TryParseDateOnly(body.WeekStartMonday, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStartMonday. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStartMonday must be a Monday." });

        var userId = body.UserId;
        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (target is null) return NotFound();
        if (target.Role == AppRole.Admin)
            return BadRequest(new AuthErrorResponse { Message = "Admin weeks are self-signed; no reviewer queue." });
        if (!ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(target.Role) &&
            !ProjectApprovalRouting.UsesFinancePartnerWeekApproval(target.Role) &&
            !ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(target.Role))
            return BadRequest(new AuthErrorResponse { Message = "Weekly approval does not apply to this account type." });

        if (!User.IsInRole(nameof(AppRole.Admin)))
        {
            var ok = false;
            if ((User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(target.Role) &&
                await ProjectApprovalRouting.PartnerMayApproveManagerTimesheetWeekAsync(db, reviewerId, userId, start, ct))
                ok = true;
            if (!ok &&
                (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesFinancePartnerWeekApproval(target.Role) &&
                await ProjectApprovalRouting.PartnerMayApproveFinanceTimesheetWeekAsync(db, reviewerId, userId, start, ct))
                ok = true;
            if (!ok &&
                (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(target.Role) &&
                await ProjectApprovalRouting.ManagerMayApproveIcTimesheetWeekAsync(db, reviewerId, userId, start, ct))
                ok = true;
            if (!ok) return Forbid();
        }

        var approval = await db.TimesheetWeekApprovals.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.WeekStartMonday == start && x.Status == TimesheetWeekApprovalStatus.Pending,
                ct);
        if (approval is null)
            return NotFound(new AuthErrorResponse { Message = "No pending submission for that week." });

        var end = start.AddDays(7);
        var ents = await db.TimesheetLines
            .AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == userId && l.WorkDate >= start && l.WorkDate < end)
            .OrderBy(l => l.WorkDate)
            .ThenBy(l => l.Client)
            .ThenBy(l => l.Project)
            .ThenBy(l => l.Task)
            .ToListAsync(ct);
        var lineRows = ents
            .Select(l => new OperationsTimesheetAiLineDto
            {
                WorkDate = l.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Client = l.Client,
                Project = l.Project,
                Task = l.Task,
                Hours = l.Hours,
                IsBillable = l.IsBillable,
                Notes = l.Notes,
            })
            .ToList();

        var weekReq = new OperationsTimesheetWeekAiReviewRequest
        {
            WeekStartMonday = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Lines = lineRows,
        };

        return Ok(await advisor.ReviewTimesheetWeekAsync(weekReq, ct, approverContext: true, subjectEmail: target.Email));
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
