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

        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
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
        try
        {
            await UpsertWeekForUser(userId, start, end, body, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AuthErrorResponse { Message = ex.Message });
        }
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
