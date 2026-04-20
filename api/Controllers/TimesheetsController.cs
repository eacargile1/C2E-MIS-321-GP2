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
public sealed class TimesheetsController(AppDbContext db) : ControllerBase
{
    /// <summary>Org-wide availability matrix for the month (FR20).</summary>
    [HttpGet("organization")]
    [Authorize(Roles = RbacRoleSets.NonIc)]
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
                var status = hours >= 8m ? "FullyBooked" : hours > 0m ? "SoftBooked" : "Available";
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

    /// <summary>IC weekly submission status for billable-hour sign-off (None until submitted).</summary>
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

    /// <summary>IC submits the current week for manager approval (billable hours).</summary>
    [HttpPost("week/submit")]
    [Authorize]
    public async Task<IActionResult> SubmitWeekForApproval([FromQuery] string weekStart, CancellationToken ct)
    {
        if (!TryParseDateOnly(weekStart, out var start))
            return BadRequest(new AuthErrorResponse { Message = "Invalid weekStart. Use YYYY-MM-DD." });
        if (start.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new AuthErrorResponse { Message = "weekStart must be a Monday." });
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();
        if (user.Role != AppRole.IC)
            return BadRequest(new AuthErrorResponse { Message = "Only IC accounts submit weekly timesheets for approval." });

        var existing = await db.TimesheetWeekApprovals.FirstOrDefaultAsync(x => x.UserId == userId && x.WeekStartMonday == start, ct);
        if (existing is { Status: TimesheetWeekApprovalStatus.Pending })
            return BadRequest(new AuthErrorResponse { Message = "This week is already pending approval." });
        if (existing is { Status: TimesheetWeekApprovalStatus.Approved })
            return BadRequest(new AuthErrorResponse { Message = "This week is already approved." });

        if (existing is not null)
            db.TimesheetWeekApprovals.Remove(existing);

        var now = DateTime.UtcNow;
        db.TimesheetWeekApprovals.Add(new TimesheetWeekApproval
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WeekStartMonday = start,
            Status = TimesheetWeekApprovalStatus.Pending,
            SubmittedAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Pending IC week submissions for manager/admin review.</summary>
    [HttpGet("approvals/pending-weeks")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<IReadOnlyList<PendingTimesheetWeekResponse>>> ListPendingTimesheetWeeks(CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var q = db.TimesheetWeekApprovals.AsNoTracking()
            .Where(a => a.Status == TimesheetWeekApprovalStatus.Pending);
        if (User.IsInRole(nameof(AppRole.Manager)) && !User.IsInRole(nameof(AppRole.Admin)))
            q = q.Where(a => db.Users.Any(u => u.Id == a.UserId && u.ManagerUserId == reviewerId));

        var rows = await q.Join(db.Users.AsNoTracking(), a => a.UserId, u => u.Id, (a, u) => new { a, u.Email })
            .OrderBy(x => x.a.SubmittedAtUtc)
            .ToListAsync(ct);

        var result = new List<PendingTimesheetWeekResponse>(rows.Count);
        foreach (var x in rows)
        {
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
    /// Full timesheet lines for an IC week that is <see cref="TimesheetWeekApprovalStatus.Pending"/> (manager = direct report only).
    /// </summary>
    [HttpGet("approvals/week/{userId:guid}/pending-review")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
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
        if (target.Role != AppRole.IC)
            return BadRequest(new AuthErrorResponse { Message = "Only IC timesheets use weekly approval." });

        if (User.IsInRole(nameof(AppRole.Manager)) && !User.IsInRole(nameof(AppRole.Admin)))
        {
            if (target.ManagerUserId != reviewerId)
                return Forbid();
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
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public Task<IActionResult> ApproveTimesheetWeek([FromRoute] Guid userId, [FromQuery] string weekStart, CancellationToken ct) =>
        ReviewTimesheetWeekAsync(userId, weekStart, TimesheetWeekApprovalStatus.Approved, ct);

    [HttpPost("approvals/week/{userId:guid}/reject")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
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

        var end = start.AddDays(7);
        if (!TryGetUserId(out var userId)) return Unauthorized();

        if (!await EnsureIcTimesheetEditableAsync(userId, start, ct))
            return BadRequest(new AuthErrorResponse
            {
                Message = "This week is pending manager approval and cannot be edited until it is approved or rejected.",
            });

        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
            await ClearIcWeekApprovalAfterSuccessfulEditAsync(userId, start, ct);
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

        var end = start.AddDays(7);
        if (!await EnsureIcTimesheetEditableAsync(userId, start, ct))
            return BadRequest(new AuthErrorResponse
            {
                Message = "This week is pending manager approval and cannot be edited until it is approved or rejected.",
            });

        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
            await ClearIcWeekApprovalAfterSuccessfulEditAsync(userId, start, ct);
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

        if (User.IsInRole(nameof(AppRole.Manager)) && !User.IsInRole(nameof(AppRole.Admin)))
        {
            var sub = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
            if (sub?.ManagerUserId != reviewerId)
                return Forbid();
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

    private async Task<bool> EnsureIcTimesheetEditableAsync(Guid userId, DateOnly weekStart, CancellationToken ct)
    {
        var isIc = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Role == AppRole.IC, ct);
        if (!isIc) return true;
        return !await db.TimesheetWeekApprovals.AnyAsync(
            x => x.UserId == userId && x.WeekStartMonday == weekStart && x.Status == TimesheetWeekApprovalStatus.Pending,
            ct);
    }

    private async Task ClearIcWeekApprovalAfterSuccessfulEditAsync(Guid userId, DateOnly weekStart, CancellationToken ct)
    {
        var isIc = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Role == AppRole.IC, ct);
        if (!isIc) return;
        var rows = await db.TimesheetWeekApprovals
            .Where(x => x.UserId == userId && x.WeekStartMonday == weekStart && x.Status != TimesheetWeekApprovalStatus.Pending)
            .ToListAsync(ct);
        if (rows.Count == 0) return;
        db.TimesheetWeekApprovals.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
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

        foreach (var (_, client, project, _, _, _, _) in normalized)
        {
            await ActiveCatalogValidation.EnsureActiveClientAndProjectAsync(db, client, project, ct);
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

    private static DateOnly WeekStartMondayForWorkDate(DateOnly workDate)
    {
        var offset = ((int)workDate.DayOfWeek + 6) % 7;
        return workDate.AddDays(-offset);
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
    /// Billable hours on a project: non-IC lines always; IC lines only when that calendar week is manager-approved.
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

        var icUserIds = lines.Where(x => x.Role == AppRole.IC).Select(x => x.UserId).Distinct().ToList();
        HashSet<(Guid UserId, DateOnly WeekStart)>? approvedIcWeeks = null;
        if (icUserIds.Count > 0)
        {
            var approvedRows = await db.TimesheetWeekApprovals.AsNoTracking()
                .Where(a =>
                    a.Status == TimesheetWeekApprovalStatus.Approved &&
                    icUserIds.Contains(a.UserId))
                .Select(a => new { a.UserId, a.WeekStartMonday })
                .ToListAsync(ct);
            approvedIcWeeks = approvedRows
                .Select(a => (a.UserId, a.WeekStartMonday))
                .ToHashSet();
        }

        decimal sumHours = 0;
        foreach (var x in lines)
        {
            if (x.Role == AppRole.IC)
            {
                if (approvedIcWeeks is null) continue;
                var mon = WeekStartMondayForWorkDate(x.WorkDate);
                if (!approvedIcWeeks.Contains((x.UserId, mon)))
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
