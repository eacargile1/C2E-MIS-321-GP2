using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace C2E.Api.Controllers;

/// <summary>Finance-only AI helpers (ledger audit, quote field suggestions).</summary>
[ApiController]
[Route("api/ai/finance")]
[Authorize(Roles = RbacRoleSets.AdminAndFinance)]
public sealed class FinanceAiController(AppDbContext db, IFinanceAiAssistant financeAi) : ControllerBase
{
    [HttpPost("ledger-audit")]
    public async Task<ActionResult<FinanceLedgerAuditResponse>> LedgerAudit(
        [FromBody] FinanceLedgerAuditRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        return Ok(await financeAi.AuditLedgerAsync(db, body, ct));
    }

    [HttpPost("quote-draft")]
    public async Task<ActionResult<FinanceQuoteDraftResponse>> QuoteDraft(
        [FromBody] FinanceQuoteDraftRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        return Ok(await financeAi.DraftQuoteAsync(db, body, ct));
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
