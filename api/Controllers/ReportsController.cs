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
        if (!TryParseDateOnly(from, out var fromDate) || !TryParseDateOnly(to, out var toDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
        if (toDate < fromDate)
            return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

        var lines = await db.TimesheetLines
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.WorkDate >= fromDate && l.WorkDate <= toDate)
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
