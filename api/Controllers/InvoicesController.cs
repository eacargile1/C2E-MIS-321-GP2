using System.Globalization;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(AppDbContext db) : ControllerBase
{
    /// <summary>Legacy no-op (kept for RBAC smoke tests).</summary>
    [HttpPost("generate")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public IActionResult LegacyGenerate() => NoContent();

    /// <summary>Draft invoice from billable timesheet hours + approved expenses for a client (Finance).</summary>
    [HttpPost("generate-draft")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<ActionResult<DraftInvoiceResponse>> GenerateDraft([FromBody] DraftInvoiceRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        if (!TryParseDateOnly(body.PeriodStart, out var start) || !TryParseDateOnly(body.PeriodEnd, out var end))
            return BadRequest(new AuthErrorResponse { Message = "Invalid periodStart/periodEnd. Use YYYY-MM-DD." });
        if (end < start)
            return BadRequest(new AuthErrorResponse { Message = "periodEnd must be on or after periodStart." });

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == body.ClientId, ct);
        if (client is null || !client.IsActive)
            return NotFound(new AuthErrorResponse { Message = "Client not found or inactive." });

        var rate = client.DefaultBillingRate ?? 0m;
        var clientNameNorm = client.Name.ToLowerInvariant();

        var billableHours = await db.TimesheetLines
            .AsNoTracking()
            .Where(l =>
                l.WorkDate >= start &&
                l.WorkDate <= end &&
                l.IsBillable &&
                l.Client.ToLower() == clientNameNorm)
            .SumAsync(l => (decimal?)l.Hours, ct) ?? 0m;

        var approvedExpenses = await db.ExpenseEntries
            .AsNoTracking()
            .Where(e =>
                e.Status == ExpenseStatus.Approved &&
                e.ExpenseDate >= start &&
                e.ExpenseDate <= end &&
                e.Client.ToLower() == clientNameNorm)
            .ToListAsync(ct);

        var lines = new List<DraftInvoiceLineDto>(2);
        if (billableHours > 0m)
        {
            var amt = billableHours * rate;
            lines.Add(new DraftInvoiceLineDto
            {
                Source = "Timesheet",
                Description = $"Billable time ({billableHours:0.##} h × client default rate)",
                Quantity = billableHours,
                Unit = "hour",
                UnitRate = rate,
                Amount = amt,
            });
        }

        foreach (var e in approvedExpenses.OrderBy(x => x.ExpenseDate))
        {
            lines.Add(new DraftInvoiceLineDto
            {
                Source = "Expense",
                Description = $"{e.ExpenseDate:yyyy-MM-dd} · {e.Project} · {e.Category} · {e.Description}",
                Quantity = 1m,
                Unit = "ea",
                UnitRate = e.Amount,
                Amount = e.Amount,
            });
        }

        var subtotal = lines.Sum(x => x.Amount);
        var note = client.DefaultBillingRate is null
            ? "Client has no default hourly rate; time lines used a 0 rate. Set DefaultBillingRate on the client for accurate billing."
            : "Draft only — not persisted as an issued invoice.";

        return Ok(new DraftInvoiceResponse
        {
            ClientId = client.Id,
            ClientName = client.Name,
            PeriodStart = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEnd = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DefaultHourlyRate = client.DefaultBillingRate,
            Lines = lines,
            Subtotal = subtotal,
            Note = note,
        });
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
