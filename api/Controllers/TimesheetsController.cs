using System.Security.Claims;
using System.Globalization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Authorization;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/timesheets")]
public sealed class TimesheetsController(AppDbContext db, TimesheetWeekWindow timesheetWeekWindow) : ControllerBase
{
    /// <summary>Org-wide availability matrix for the month (FR20). Read-only for all authenticated roles including IC.</summary>
    [HttpGet("organization")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ResourceTrackerEmployeeRowResponse>>> GetOrganization(
        [FromQuery] string monthStart,
        CancellationToken ct)
    {
        if (!TryParseDateOnly(monthStart, out var parsed))
            return BadRequest(new AuthErrorResponse { Message = "Invalid monthStart. Use YYYY-MM-DD." });

        var start = new DateOnly(parsed.Year, parsed.Month, 1);
        var end = start.AddMonths(1);
        var userRows = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.Role })
            .ToListAsync(ct);

        var lines = await db.TimesheetLines
            .AsNoTracking()
            .Where(l => !l.IsDeleted && l.WorkDate >= start && l.WorkDate < end)
            .GroupBy(l => new { l.UserId, l.WorkDate })
            .Select(g => new { g.Key.UserId, g.Key.WorkDate, Hours = g.Sum(x => x.Hours) })
            .ToListAsync(ct);

        var approvedPto = await db.PtoRequests.AsNoTracking()
            .Where(p => p.Status == PtoRequestStatus.Approved && p.StartDate < end && p.EndDate >= start)
            .Select(p => new { p.UserId, p.StartDate, p.EndDate })
            .ToListAsync(ct);

        var ptoByUserDate = new HashSet<(Guid UserId, DateOnly Day)>();
        foreach (var p in approvedPto)
        {
            var from = p.StartDate < start ? start : p.StartDate;
            var to = p.EndDate >= end ? end.AddDays(-1) : p.EndDate;
            for (var d = from; d <= to; d = d.AddDays(1))
                ptoByUserDate.Add((p.UserId, d));
        }

        var byUserDate = lines.ToDictionary(x => (x.UserId, x.WorkDate), x => x.Hours);
        var dayCount = end.DayNumber - start.DayNumber;
        var result = new List<ResourceTrackerEmployeeRowResponse>(userRows.Count);

        foreach (var u in userRows)
        {
            var days = new List<ResourceTrackerDayResponse>(dayCount);
            for (var i = 0; i < dayCount; i++)
            {
                var day = start.AddDays(i);
                var hours = byUserDate.TryGetValue((u.Id, day), out var h) ? h : 0m;
                var status = ptoByUserDate.Contains((u.Id, day))
                    ? "PTO"
                    : hours >= 8m ? "FullyBooked" : hours > 0m ? "SoftBooked" : "Available";
                days.Add(new ResourceTrackerDayResponse
                {
                    Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Status = status,
                    Hours = hours,
                });
            }

            result.Add(new ResourceTrackerEmployeeRowResponse
            {
                UserId = u.Id,
                Email = u.Email,
                Role = u.Role.ToString(),
                Days = days,
            });
        }

        return Ok(result);
    }

    /// <summary>IC or Manager weekly submission status for billable-hour sign-off (None until submitted).</summary>
    [HttpGet("week/status")]
    [Authorize]
    public async Task<ActionResult<TimesheetWeekStatusResponse>> GetWeekStatus(
        [FromQuery] string weekStart,
        CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErr0)
            return BadRequest(new AuthErrorResponse { Message = winErr0 });
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var weekEnd = start.AddDays(7);
        var (total, billable) = await SumWeekHoursAsync(userId, start, weekEnd, ct);
        var appr = await db.TimesheetWeekApprovals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.WeekStartMonday == start, ct);
        var status = appr is null ? "None" : appr.Status.ToString();
        return Ok(new TimesheetWeekStatusResponse
        {
            WeekStart = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status = status,
            TotalHours = total,
            BillableHours = billable,
            SubmittedAtUtc = appr?.SubmittedAtUtc,
            ReviewedAtUtc = appr?.ReviewedAtUtc,
        });
    }

    /// <summary>
    /// IC / Finance / Manager / Partner: project delivery manager, else engagement partner, else profile superior; Partner submitters resolve to self.
    /// When the resolved approver is the submitter, the week is signed immediately (no pending queue).
    /// </summary>
    [HttpPost("week/submit")]
    [Authorize]
    public async Task<IActionResult> SubmitWeekForApproval([FromQuery] string weekStart, CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErrSubmit)
            return BadRequest(new AuthErrorResponse { Message = winErrSubmit });
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        var existing = await db.TimesheetWeekApprovals.FirstOrDefaultAsync(x => x.UserId == userId && x.WeekStartMonday == start, ct);
        if (existing is { Status: TimesheetWeekApprovalStatus.Pending })
            return BadRequest(new AuthErrorResponse { Message = "This week is already pending approval." });
        if (existing is { Status: TimesheetWeekApprovalStatus.Approved } && user.Role != AppRole.Admin)
            return BadRequest(new AuthErrorResponse { Message = "This week is already approved." });

        if (existing is not null)
            db.TimesheetWeekApprovals.Remove(existing);

        var now = DateTime.UtcNow;

        // Org admins self-sign weekly billable totals (no reviewer queue).
        if (user.Role == AppRole.Admin)
        {
            db.TimesheetWeekApprovals.Add(new TimesheetWeekApproval
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WeekStartMonday = start,
                Status = TimesheetWeekApprovalStatus.Approved,
                SubmittedAtUtc = now,
                ReviewedByUserId = userId,
                ReviewedAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        HashSet<Guid>? resolvedApprovers = null;

        if (ProjectApprovalRouting.UsesFinancePartnerWeekApproval(user.Role))
        {
            var err = await ProjectApprovalRouting.ValidateFinanceWeekSubmitAsync(db, userId, start, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            var (ap, _) = await ProjectApprovalRouting.ResolveFinanceWeekApproverIdsAsync(db, userId, start, ct);
            resolvedApprovers = ap;
        }
        else if (ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(user.Role))
        {
            var err = await ProjectApprovalRouting.ValidateIcWeekSubmitAsync(db, userId, start, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            var (ap, _) = await ProjectApprovalRouting.ResolveIcWeekApproverIdsAsync(db, userId, start, ct);
            resolvedApprovers = ap;
        }
        else if (ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(user.Role))
        {
            var err = await ProjectApprovalRouting.ValidateManagerWeekSubmitAsync(db, userId, start, ct);
            if (err is not null)
                return BadRequest(new AuthErrorResponse { Message = err });
            var (ap, _) = await ProjectApprovalRouting.ResolveManagerPartnerWeekApproverIdsAsync(db, userId, start, ct);
            resolvedApprovers = ap;
        }
        else
            return BadRequest(new AuthErrorResponse { Message = "Weekly submit is not configured for this role." });

        var selfSign =
            resolvedApprovers is { Count: 1 } ids && ids.Contains(userId);

        db.TimesheetWeekApprovals.Add(new TimesheetWeekApproval
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WeekStartMonday = start,
            Status = selfSign ? TimesheetWeekApprovalStatus.Approved : TimesheetWeekApprovalStatus.Pending,
            SubmittedAtUtc = now,
            ReviewedByUserId = selfSign ? userId : null,
            ReviewedAtUtc = selfSign ? now : null,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Pending IC / Finance / Manager weeks (project DM / EP / profile superior); self-signed weeks never appear here.</summary>
    [HttpGet("approvals/pending-weeks")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<IReadOnlyList<PendingTimesheetWeekResponse>>> ListPendingTimesheetWeeks(CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var rows = await db.TimesheetWeekApprovals.AsNoTracking()
            .Where(a => a.Status == TimesheetWeekApprovalStatus.Pending)
            .Join(db.Users.AsNoTracking(), a => a.UserId, u => u.Id, (a, u) => new { a, u.Email, u.Role })
            .OrderBy(x => x.a.SubmittedAtUtc)
            .ToListAsync(ct);

        var result = new List<PendingTimesheetWeekResponse>(rows.Count);
        foreach (var x in rows)
        {
            if (x.Role == AppRole.Admin) continue;

            if (!User.IsInRole(nameof(AppRole.Admin)))
            {
                var ok = false;
                if ((User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                    ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(x.Role) &&
                    await ProjectApprovalRouting.PartnerMayApproveManagerTimesheetWeekAsync(
                        db, reviewerId, x.a.UserId, x.a.WeekStartMonday, ct))
                    ok = true;
                if (!ok &&
                    (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                    ProjectApprovalRouting.UsesFinancePartnerWeekApproval(x.Role) &&
                    await ProjectApprovalRouting.PartnerMayApproveFinanceTimesheetWeekAsync(
                        db, reviewerId, x.a.UserId, x.a.WeekStartMonday, ct))
                    ok = true;
                if (!ok &&
                    (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                    ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(x.Role) &&
                    await ProjectApprovalRouting.ManagerMayApproveIcTimesheetWeekAsync(
                        db, reviewerId, x.a.UserId, x.a.WeekStartMonday, ct))
                    ok = true;
                if (!ok) continue;
            }

            var weekEnd = x.a.WeekStartMonday.AddDays(7);
            var (total, billable) = await SumWeekHoursAsync(x.a.UserId, x.a.WeekStartMonday, weekEnd, ct);
            result.Add(new PendingTimesheetWeekResponse
            {
                UserId = x.a.UserId,
                UserEmail = x.Email,
                WeekStart = x.a.WeekStartMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TotalHours = total,
                BillableHours = billable,
                SubmittedAtUtc = x.a.SubmittedAtUtc,
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Full timesheet lines for a pending IC / Finance / Manager week (project DM / EP / profile superior).
    /// </summary>
    [HttpGet("approvals/week/{userId:guid}/pending-review")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<TimesheetPendingWeekReviewResponse>> GetPendingWeekForReview(
        [FromRoute] Guid userId,
        [FromQuery] string weekStart,
        CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (target is null) return NotFound();
        if (target.Role == AppRole.Admin)
            return BadRequest(new AuthErrorResponse { Message = "Admin weeks are self-signed in the app; there is no reviewer queue for them." });
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
        var lines = await GetWeekForUser(userId, start, end, ct);
        var budgetBars = await BuildProjectBudgetBarsAsync(lines, ct);
        return Ok(new TimesheetPendingWeekReviewResponse
        {
            UserId = userId,
            UserEmail = target.Email,
            WeekStart = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SubmittedAtUtc = approval.SubmittedAtUtc,
            Lines = lines,
            ProjectBudgetBars = budgetBars,
        });
    }

    [HttpPost("approvals/week/{userId:guid}/approve")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public Task<IActionResult> ApproveTimesheetWeek([FromRoute] Guid userId, [FromQuery] string weekStart, CancellationToken ct) =>
        ReviewTimesheetWeekAsync(userId, weekStart, TimesheetWeekApprovalStatus.Approved, ct);

    [HttpPost("approvals/week/{userId:guid}/reject")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public Task<IActionResult> RejectTimesheetWeek([FromRoute] Guid userId, [FromQuery] string weekStart, CancellationToken ct) =>
        ReviewTimesheetWeekAsync(userId, weekStart, TimesheetWeekApprovalStatus.Rejected, ct);

    /// <summary>Employee weekly timesheet lines (FR5). Week is Monday–Sunday (7 days starting at weekStart).</summary>
    [HttpGet("week")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<TimesheetLineResponse>>> GetWeek(
        [FromQuery] string weekStart,
        CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErrGet)
            return BadRequest(new AuthErrorResponse { Message = winErrGet });

        var end = start.AddDays(7);
        if (!TryGetUserId(out var userId)) return Unauthorized();

        return Ok(await GetWeekForUser(userId, start, end, ct));
    }

    /// <summary>Replace/upsert the signed-in user's lines for a week (FR5).</summary>
    [HttpPut("week")]
    [Authorize]
    public async Task<IActionResult> PutWeek(
        [FromQuery] string weekStart,
        [FromBody] List<TimesheetLineUpsertRequest> body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError(ModelState) ?? "Invalid request." });

        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErrPut)
            return BadRequest(new AuthErrorResponse { Message = winErrPut });

        var end = start.AddDays(7);
        if (!TryGetUserId(out var userId)) return Unauthorized();

        if (!await EnsureTimesheetWeekEditableAsync(userId, start, ct))
            return BadRequest(new AuthErrorResponse
            {
                Message = "This week is pending approval and cannot be edited until it is approved or rejected.",
            });

        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
            await ClearNonPendingWeekApprovalAfterSuccessfulEditAsync(userId, start, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AuthErrorResponse { Message = ex.Message });
        }
    }

    /// <summary>
    /// User-scoped week endpoint to support 403/404 semantics for non-owners (AC4/DoD).
    /// </summary>
    [HttpGet("users/{userId:guid}/week")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<TimesheetLineResponse>>> GetWeekForUserRoute(
        [FromRoute] Guid userId,
        [FromQuery] string weekStart,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var currentUserId)) return Unauthorized();
        if (currentUserId != userId) return Forbid();

        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErrGetScoped)
            return BadRequest(new AuthErrorResponse { Message = winErrGetScoped });

        var end = start.AddDays(7);
        return Ok(await GetWeekForUser(userId, start, end, ct));
    }

    /// <summary>
    /// User-scoped week endpoint to support 403/404 semantics for non-owners (AC4/DoD).
    /// </summary>
    [HttpPut("users/{userId:guid}/week")]
    [Authorize]
    public async Task<IActionResult> PutWeekForUserRoute(
        [FromRoute] Guid userId,
        [FromQuery] string weekStart,
        [FromBody] List<TimesheetLineUpsertRequest> body,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var currentUserId)) return Unauthorized();
        if (currentUserId != userId) return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError(ModelState) ?? "Invalid request." });

        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (WeekEntryWindowError(start) is { } winErrPutScoped)
            return BadRequest(new AuthErrorResponse { Message = winErrPutScoped });

        var end = start.AddDays(7);
        if (!await EnsureTimesheetWeekEditableAsync(userId, start, ct))
            return BadRequest(new AuthErrorResponse
            {
                Message = "This week is pending approval and cannot be edited until it is approved or rejected.",
            });

        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
            await ClearNonPendingWeekApprovalAfterSuccessfulEditAsync(userId, start, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AuthErrorResponse { Message = ex.Message });
        }
    }

    private async Task<IActionResult> ReviewTimesheetWeekAsync(
        Guid targetUserId,
        string weekStart,
        TimesheetWeekApprovalStatus nextStatus,
        CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var row = await db.TimesheetWeekApprovals.FirstOrDefaultAsync(
            x => x.UserId == targetUserId && x.WeekStartMonday == start && x.Status == TimesheetWeekApprovalStatus.Pending,
            ct);
        if (row is null) return NotFound();

        if (!User.IsInRole(nameof(AppRole.Admin)))
        {
            var sub = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
            if (sub is null) return Forbid();

            var ok = false;
            if ((User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(sub.Role) &&
                await ProjectApprovalRouting.PartnerMayApproveManagerTimesheetWeekAsync(db, reviewerId, targetUserId, start, ct))
                ok = true;
            if (!ok &&
                (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesFinancePartnerWeekApproval(sub.Role) &&
                await ProjectApprovalRouting.PartnerMayApproveFinanceTimesheetWeekAsync(db, reviewerId, targetUserId, start, ct))
                ok = true;
            if (!ok &&
                (User.IsInRole(nameof(AppRole.Manager)) || User.IsInRole(nameof(AppRole.Partner))) &&
                ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(sub.Role) &&
                await ProjectApprovalRouting.ManagerMayApproveIcTimesheetWeekAsync(db, reviewerId, targetUserId, start, ct))
                ok = true;
            if (!ok) return Forbid();
        }

        row.Status = nextStatus;
        row.ReviewedByUserId = reviewerId;
        row.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(decimal total, decimal billable)> SumWeekHoursAsync(
        Guid userId,
        DateOnly start,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var lines = await db.TimesheetLines.AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == userId && l.WorkDate >= start && l.WorkDate < endExclusive)
            .Select(l => new { l.Hours, l.IsBillable })
            .ToListAsync(ct);
        var total = lines.Sum(x => x.Hours);
        var billable = lines.Where(x => x.IsBillable).Sum(x => x.Hours);
        return (total, billable);
    }

    private async Task<bool> EnsureTimesheetWeekEditableAsync(Guid userId, DateOnly weekStart, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
        if (role == AppRole.Admin) return true;
        if (!ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(role) &&
            !ProjectApprovalRouting.UsesFinancePartnerWeekApproval(role) &&
            !ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(role))
            return true;
        return !await db.TimesheetWeekApprovals.AnyAsync(
            x => x.UserId == userId && x.WeekStartMonday == weekStart && x.Status == TimesheetWeekApprovalStatus.Pending,
            ct);
    }

    private async Task ClearNonPendingWeekApprovalAfterSuccessfulEditAsync(Guid userId, DateOnly weekStart, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
        if (!ProjectApprovalRouting.UsesDeliveryManagerWeekApproval(role) &&
            !ProjectApprovalRouting.UsesFinancePartnerWeekApproval(role) &&
            !ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(role))
            return;
        var rows = await db.TimesheetWeekApprovals
            .Where(x => x.UserId == userId && x.WeekStartMonday == weekStart && x.Status != TimesheetWeekApprovalStatus.Pending)
            .ToListAsync(ct);
        if (rows.Count == 0) return;
        db.TimesheetWeekApprovals.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    private string? WeekEntryWindowError(DateOnly weekStartMonday)
    {
        if (timesheetWeekWindow.IsWeekStartAllowed(weekStartMonday))
            return null;

        var (min, max) = timesheetWeekWindow.AllowedWeekMondayRange();
        return
            $"That timesheet week is outside the allowed range (Monday weeks from {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} through {max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, UTC). Entries are limited to about one calendar month before and after the week that contains today.";
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

    private async Task<List<TimesheetLineResponse>> GetWeekForUser(Guid userId, DateOnly start, DateOnly end, CancellationToken ct) =>
        await db.TimesheetLines
            .AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == userId && l.WorkDate >= start && l.WorkDate < end)
            .OrderBy(l => l.WorkDate)
            .ThenBy(l => l.Client)
            .ThenBy(l => l.Project)
            .ThenBy(l => l.Task)
            .Select(l => new TimesheetLineResponse
            {
                WorkDate = l.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Client = l.Client,
                Project = l.Project,
                Task = l.Task,
                Hours = l.Hours,
                IsBillable = l.IsBillable,
                Notes = l.Notes,
            })
            .ToListAsync(ct);

    private async Task UpsertWeekForUser(
        Guid userId,
        DateOnly start,
        DateOnly end,
        List<TimesheetLineUpsertRequest> body,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        static string NormRequired(string v) => v.Trim();

        var normalized = new List<(DateOnly workDate, string client, string project, string task, decimal hours, bool billable, string? notes)>(
            body.Count);

        foreach (var line in body)
        {
            if (!TryParseDateOnly(line.WorkDate, out var workDate))
                throw new InvalidOperationException("Invalid workDate. Use YYYY-MM-DD.");
            if (workDate < start || workDate >= end)
                throw new InvalidOperationException("workDate must fall within the requested week.");

            var client = NormRequired(line.Client);
            var project = NormRequired(line.Project);
            var task = NormRequired(line.Task);

            if (client.Length == 0 || project.Length == 0 || task.Length == 0)
                throw new InvalidOperationException("client, project, and task are required.");

            if (line.Hours <= 0 || line.Hours > 24)
                throw new InvalidOperationException("hours must be > 0 and <= 24.");
            var quarter = line.Hours * 4m;
            if (quarter % 1m != 0m)
                throw new InvalidOperationException("hours must be in 0.25 increments.");

            var notes = line.Notes?.Trim();
            if (notes is { Length: 0 }) notes = null;

            normalized.Add((workDate, client, project, task, line.Hours, line.IsBillable, notes));
        }

        var submitterRole = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .FirstAsync(ct);
        var catalogEnforced = await db.Clients.AnyAsync(c => c.IsActive, ct);

        foreach (var (_, client, project, _, _, _, _) in normalized)
        {
            await ActiveCatalogValidation.EnsureActiveClientAndProjectAsync(db, client, project, ct);
            if (catalogEnforced && submitterRole == AppRole.IC &&
                !await IcCatalogAccess.MayUseClientProjectAsync(db, userId, client, project, ct))
            {
                throw new InvalidOperationException(
                    $"You are not assigned to client \"{client}\" / project \"{project}\". Ask a Partner or Admin to add you to the client roster, project roster, or project team.");
            }
        }

        var existing = await db.TimesheetLines
            .Where(l => !l.IsDeleted && l.UserId == userId && l.WorkDate >= start && l.WorkDate < end)
            .ToListAsync(ct);

        var byKey = existing.ToDictionary(
            l => (l.WorkDate, l.Client, l.Project, l.Task),
            l => l);

        var incomingKeys = new HashSet<(DateOnly, string, string, string)>();

        foreach (var (workDate, client, project, task, hours, billable, notes) in normalized)
        {
            var key = (workDate, client, project, task);
            if (!incomingKeys.Add(key))
                throw new InvalidOperationException("Duplicate lines are not allowed for the same workDate/client/project/task.");

            if (byKey.TryGetValue(key, out var row))
            {
                row.Hours = hours;
                row.IsBillable = billable;
                row.Notes = notes;
                row.UpdatedAtUtc = now;
            }
            else
            {
                db.TimesheetLines.Add(new TimesheetLine
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    WorkDate = workDate,
                    Client = client,
                    Project = project,
                    Task = task,
                    Hours = hours,
                    IsBillable = billable,
                    Notes = notes,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
        }

        foreach (var existingRow in existing)
        {
            var key = (existingRow.WorkDate, existingRow.Client, existingRow.Project, existingRow.Task);
            if (incomingKeys.Contains(key)) continue;

            existingRow.IsDeleted = true;
            existingRow.DeletedAtUtc = now;
            existingRow.UpdatedAtUtc = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Could not save timesheet.");
        }
    }

    private async Task<IReadOnlyList<ProjectBudgetBarDto>> BuildProjectBudgetBarsAsync(
        IReadOnlyList<TimesheetLineResponse> pendingLines,
        CancellationToken ct)
    {
        var billablePairs = pendingLines
            .Where(l => l.IsBillable && l.Client.Trim().Length > 0 && l.Project.Trim().Length > 0)
            .Select(l => (Client: l.Client.Trim(), Project: l.Project.Trim()))
            .Distinct()
            .ToList();
        if (billablePairs.Count == 0)
            return [];

        var catalog = await db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.IsActive && p.Client != null && p.Client.IsActive)
            .ToListAsync(ct);

        var bars = new List<ProjectBudgetBarDto>(billablePairs.Count);
        foreach (var (cName, pName) in billablePairs)
        {
            var proj = catalog.FirstOrDefault(p =>
                p.Client is not null &&
                string.Equals(p.Client.Name, cName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Name, pName, StringComparison.OrdinalIgnoreCase));

            var pendingHours = pendingLines
                .Where(l =>
                    l.IsBillable &&
                    string.Equals(l.Client.Trim(), cName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(l.Project.Trim(), pName, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.Hours);

            if (proj is null)
            {
                bars.Add(new ProjectBudgetBarDto
                {
                    ClientName = cName,
                    ProjectName = pName,
                    BudgetAmount = 0,
                    DefaultHourlyRate = null,
                    ConsumedBillableAmount = 0,
                    PendingSubmissionBillableAmount = 0,
                    PendingBillableHours = pendingHours,
                    CatalogMatched = false,
                });
                continue;
            }

            var clientEntity = proj.Client!;
            var canonicalClient = clientEntity.Name;
            var canonicalProject = proj.Name;
            var hourly = clientEntity.DefaultBillingRate;
            var rate = hourly ?? 0m;

            var consumedHours = await SumConsumedBillableHoursForProjectAsync(canonicalClient, canonicalProject, ct);
            var consumedDollars = consumedHours * rate;
            var pendingDollars = pendingHours * rate;

            bars.Add(new ProjectBudgetBarDto
            {
                ClientName = canonicalClient,
                ProjectName = canonicalProject,
                BudgetAmount = proj.BudgetAmount,
                DefaultHourlyRate = hourly,
                ConsumedBillableAmount = consumedDollars,
                PendingSubmissionBillableAmount = pendingDollars,
                PendingBillableHours = pendingHours,
                CatalogMatched = true,
            });
        }

        return bars
            .OrderBy(b => b.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Billable hours on a project: IC lines when that calendar week is delivery-manager-approved; Finance when partner-approved;
    /// Manager / Partner when engagement-partner-approved; Admin always; other roles count immediately.
    /// </summary>
    private async Task<decimal> SumConsumedBillableHoursForProjectAsync(
        string clientName,
        string projectName,
        CancellationToken ct)
    {
        var lines = await (
            from l in db.TimesheetLines.AsNoTracking()
            join u in db.Users.AsNoTracking() on l.UserId equals u.Id
            where !l.IsDeleted && l.IsBillable && l.Client == clientName && l.Project == projectName
            select new { l.UserId, l.WorkDate, l.Hours, u.Role }
        ).ToListAsync(ct);

        if (lines.Count == 0)
            return 0m;

        var icFinanceWeekGateUserIds = lines
            .Where(x => x.Role != AppRole.Admin && (x.Role == AppRole.IC || x.Role == AppRole.Finance))
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
        HashSet<(Guid UserId, DateOnly WeekStart)>? approvedIcFinanceWeeks = null;
        if (icFinanceWeekGateUserIds.Count > 0)
        {
            var approvedRows = await db.TimesheetWeekApprovals.AsNoTracking()
                .Where(a =>
                    a.Status == TimesheetWeekApprovalStatus.Approved &&
                    icFinanceWeekGateUserIds.Contains(a.UserId))
                .Select(a => new { a.UserId, a.WeekStartMonday })
                .ToListAsync(ct);
            approvedIcFinanceWeeks = approvedRows
                .Select(a => (a.UserId, a.WeekStartMonday))
                .ToHashSet();
        }

        var engagementPathUserIds = lines
            .Where(x => ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(x.Role))
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
        HashSet<(Guid UserId, DateOnly WeekStart)>? approvedEngagementPathWeeks = null;
        if (engagementPathUserIds.Count > 0)
        {
            var approvedMgrRows = await db.TimesheetWeekApprovals.AsNoTracking()
                .Where(a =>
                    a.Status == TimesheetWeekApprovalStatus.Approved &&
                    engagementPathUserIds.Contains(a.UserId))
                .Select(a => new { a.UserId, a.WeekStartMonday })
                .ToListAsync(ct);
            approvedEngagementPathWeeks = approvedMgrRows
                .Select(a => (a.UserId, a.WeekStartMonday))
                .ToHashSet();
        }

        decimal sumHours = 0;
        foreach (var x in lines)
        {
            if (x.Role == AppRole.Admin)
            {
                sumHours += x.Hours;
                continue;
            }

            if (x.Role == AppRole.IC || x.Role == AppRole.Finance)
            {
                if (approvedIcFinanceWeeks is null) continue;
                var mon = TimesheetWeekWindow.MondayOfWeekContaining(x.WorkDate);
                if (!approvedIcFinanceWeeks.Contains((x.UserId, mon)))
                    continue;
            }
            else if (ProjectApprovalRouting.UsesEngagementPartnerWeekApproval(x.Role))
            {
                if (approvedEngagementPathWeeks is null) continue;
                var mon = TimesheetWeekWindow.MondayOfWeekContaining(x.WorkDate);
                if (!approvedEngagementPathWeeks.Contains((x.UserId, mon)))
                    continue;
            }

            sumHours += x.Hours;
        }

        return sumHours;
    }

    private static string? FirstModelError(ModelStateDictionary modelState)
    {
        foreach (var (_, state) in modelState)
        {
            if (state.Errors.Count == 0) continue;
            var msg = state.Errors[0].ErrorMessage;
            if (!string.IsNullOrWhiteSpace(msg)) return msg;
        }
        return null;
    }
}
