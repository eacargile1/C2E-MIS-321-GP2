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
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Personal time + expense rollups for a date range (inclusive).</summary>
    [HttpGet("personal-summary")]
    [Authorize(Roles = RbacRoleSets.NonIc)]
    public async Task<ActionResult<PersonalSummaryResponse>> PersonalSummary(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseDateOnly(from ?? "", out var fromDate) || !TryParseDateOnly(to ?? "", out var toDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
        if (toDate < fromDate)
            return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

        var lines = await db.TimesheetLines
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.WorkDate >= fromDate && l.WorkDate <= toDate && !l.IsDeleted)
            .ToListAsync(ct);

        var totalHours = lines.Sum(l => l.Hours);
        var billableHours = lines.Where(l => l.IsBillable).Sum(l => l.Hours);
        var nonBillableHours = totalHours - billableHours;

        var expenses = await db.ExpenseEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.ExpenseDate >= fromDate && e.ExpenseDate <= toDate)
            .ToListAsync(ct);

        static decimal SumByStatus(IEnumerable<ExpenseEntry> xs, ExpenseStatus s) =>
            xs.Where(x => x.Status == s).Sum(x => x.Amount);

        return Ok(new PersonalSummaryResponse
        {
            From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalHours = totalHours,
            BillableHours = billableHours,
            NonBillableHours = nonBillableHours,
            TimesheetLineCount = lines.Count,
            ExpensePendingTotal = SumByStatus(expenses, ExpenseStatus.Pending),
            ExpenseApprovedTotal = SumByStatus(expenses, ExpenseStatus.Approved),
            ExpenseRejectedTotal = SumByStatus(expenses, ExpenseStatus.Rejected),
            ExpenseCount = expenses.Count,
        });
    }

    /// <summary>Per client/project hour breakdown for a date range (inclusive); line set matches <see cref="PersonalSummary"/>.</summary>
    [HttpGet("personal-detail")]
    [Authorize(Roles = RbacRoleSets.NonIc)]
    public async Task<ActionResult<PersonalDetailResponse>> PersonalDetail(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseDateOnly(from ?? "", out var fromDate) || !TryParseDateOnly(to ?? "", out var toDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
        if (toDate < fromDate)
            return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

        var lines = await db.TimesheetLines
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.WorkDate >= fromDate && l.WorkDate <= toDate && !l.IsDeleted)
            .ToListAsync(ct);

        var rows = lines
            .GroupBy(l => (l.Client, l.Project))
            .Select(g =>
            {
                var total = g.Sum(l => l.Hours);
                var bill = g.Where(l => l.IsBillable).Sum(l => l.Hours);
                return new PersonalDetailProjectRow
                {
                    Client = g.Key.Client,
                    Project = g.Key.Project,
                    TotalHours = total,
                    BillableHours = bill,
                    NonBillableHours = total - bill,
                };
            })
            .OrderByDescending(r => r.TotalHours)
            .ToList();

        return Ok(new PersonalDetailResponse
        {
            From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rows = rows,
        });
    }

    /// <summary>Team hours + expense rollup per direct report (Manager) or all users (Admin).</summary>
    [HttpGet("team-summary")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<TeamSummaryResponse>> TeamSummary(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseDateOnly(from ?? "", out var fromDate) || !TryParseDateOnly(to ?? "", out var toDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
        if (toDate < fromDate)
            return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

        List<AppUser> members;
        if (User.IsInRole(nameof(AppRole.Admin)))
        {
            members = await db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .ThenBy(u => u.Email)
                .ToListAsync(ct);
        }
        else
        {
            members = await db.Users
                .AsNoTracking()
                .Where(u => u.ManagerUserId == userId && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .ThenBy(u => u.Email)
                .ToListAsync(ct);
        }

        var memberIds = members.Select(m => m.Id).ToList();

        var lines = await db.TimesheetLines
            .AsNoTracking()
            .Where(l =>
                memberIds.Contains(l.UserId)
                && l.WorkDate >= fromDate
                && l.WorkDate <= toDate
                && !l.IsDeleted)
            .ToListAsync(ct);

        var expenses = await db.ExpenseEntries
            .AsNoTracking()
            .Where(e =>
                memberIds.Contains(e.UserId)
                && e.ExpenseDate >= fromDate
                && e.ExpenseDate <= toDate)
            .ToListAsync(ct);

        var linesByUser = lines
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var expensesByUser = expenses
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        static decimal SumByStatus(List<ExpenseEntry> xs, ExpenseStatus s) =>
            xs.Where(x => x.Status == s).Sum(x => x.Amount);

        var rows = members
            .Select(m =>
            {
                var ul = linesByUser.GetValueOrDefault(m.Id, []);
                var ue = expensesByUser.GetValueOrDefault(m.Id, []);
                var total = ul.Sum(l => l.Hours);
                var billable = ul.Where(l => l.IsBillable).Sum(l => l.Hours);

                return new TeamMemberSummaryRow
                {
                    UserId = m.Id,
                    Email = m.Email,
                    DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Email : m.DisplayName,
                    Role = m.Role.ToString(),
                    TotalHours = total,
                    BillableHours = billable,
                    NonBillableHours = total - billable,
                    TimesheetLineCount = ul.Count,
                    ExpenseCount = ue.Count,
                    ExpensePendingTotal = SumByStatus(ue, ExpenseStatus.Pending),
                    ExpenseApprovedTotal = SumByStatus(ue, ExpenseStatus.Approved),
                };
            })
            .OrderByDescending(r => r.TotalHours)
            .ToList();

        return Ok(new TeamSummaryResponse
        {
            From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rows = rows,
        });
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
}
