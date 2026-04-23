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
[Route("api/finance/insights")]
[Authorize(Roles = RbacRoleSets.AdminAndFinance)]
public sealed class FinanceInsightsController(AppDbContext db, IFinanceExpenseAiNarrativeService narratives) : ControllerBase
{
    /// <summary>LLM or heuristic narrative over approved expense aggregates for a project window.</summary>
    [HttpPost("expense-narrative")]
    public async Task<ActionResult<FinanceExpenseAiResponse>> ExpenseNarrative(
        [FromBody] FinanceExpenseAiRequest body,
        CancellationToken ct)
    {
        if (body.ProjectId == Guid.Empty)
            return BadRequest(new AuthErrorResponse { Message = "projectId is required." });
        if (!TryParseDate(body.PeriodStart, out var start) || !TryParseDate(body.PeriodEnd, out var end))
            return BadRequest(new AuthErrorResponse { Message = "Invalid periodStart/periodEnd. Use YYYY-MM-DD." });
        if (end < start)
            return BadRequest(new AuthErrorResponse { Message = "periodEnd must be on or after periodStart." });
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var role = GetUserRole();
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == body.ProjectId, ct);
        if (p is null || !p.IsActive)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });
        if (role == AppRole.Finance && p.AssignedFinanceUserId != userId)
            return Forbid();

        var (text, source) = await narratives.BuildNarrativeAsync(body.ProjectId, start, end, ct);
        return Ok(new FinanceExpenseAiResponse { Narrative = text, Source = source });
    }

    private static bool TryParseDate(string input, out DateOnly date) =>
        DateOnly.TryParseExact(
            input.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

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

    private AppRole GetUserRole()
    {
        var r = User.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<AppRole>(r, out var role) ? role : AppRole.IC;
    }
}
