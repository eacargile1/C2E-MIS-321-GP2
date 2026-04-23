using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
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

    /// <summary>Persist one invoice covering all approved expenses on a catalog project in the date range.</summary>
    [HttpPost("issue-project-approved-expenses")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<ActionResult<IssueProjectInvoiceResponse>> IssueProjectApprovedExpenses(
        [FromBody] IssueProjectInvoiceRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (body.ProjectId == Guid.Empty)
            return BadRequest(new AuthErrorResponse { Message = "projectId is required." });
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        if (!TryParseDateOnly(body.PeriodStart, out var start) || !TryParseDateOnly(body.PeriodEnd, out var end))
            return BadRequest(new AuthErrorResponse { Message = "Invalid periodStart/periodEnd. Use YYYY-MM-DD." });
        if (end < start)
            return BadRequest(new AuthErrorResponse { Message = "periodEnd must be on or after periodStart." });

        var role = GetUserRole();
        var p = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == body.ProjectId, ct);
        if (p is null || p.Client is null || !p.IsActive)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });
        if (!CanIssueInvoice(userId, role, p))
            return Forbid();

        var expenses = await LoadApprovedProjectExpensesAsync(p, start, end, ct);
        if (expenses.Count == 0)
            return BadRequest(new AuthErrorResponse { Message = "No approved expenses in this period for that project." });

        var issueNumber = NextIssueNumber();
        var now = DateTime.UtcNow;
        var invoice = new IssuedInvoice
        {
            Id = Guid.NewGuid(),
            Kind = IssuedInvoiceKind.ProjectApprovedExpenses,
            ProjectId = p.Id,
            PayeeUserId = null,
            PeriodStart = start,
            PeriodEnd = end,
            IssueNumber = issueNumber,
            IssuedAtUtc = now,
            IssuedByUserId = userId,
            TotalAmount = expenses.Sum(e => e.Amount),
        };

        var order = 0;
        foreach (var e in expenses.OrderBy(x => x.ExpenseDate).ThenBy(x => x.UserId))
        {
            invoice.Lines.Add(new IssuedInvoiceLine
            {
                Id = Guid.NewGuid(),
                ExpenseEntryId = e.Id,
                Description = $"{e.ExpenseDate:yyyy-MM-dd} · {e.Category} · {e.Description}",
                Amount = e.Amount,
                SortOrder = order++,
            });
        }

        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return Ok(new IssueProjectInvoiceResponse
        {
            InvoiceId = invoice.Id,
            IssueNumber = invoice.IssueNumber,
            TotalAmount = invoice.TotalAmount,
            LineCount = invoice.Lines.Count,
        });
    }

    /// <summary>One persisted invoice per submitter with approved expenses on the project in range (payout pack).</summary>
    [HttpPost("issue-project-payouts-by-user")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<ActionResult<IssuePayoutInvoicesResponse>> IssueProjectPayoutsByUser(
        [FromBody] IssueProjectInvoiceRequest body,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (body.ProjectId == Guid.Empty)
            return BadRequest(new AuthErrorResponse { Message = "projectId is required." });
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        if (!TryParseDateOnly(body.PeriodStart, out var start) || !TryParseDateOnly(body.PeriodEnd, out var end))
            return BadRequest(new AuthErrorResponse { Message = "Invalid periodStart/periodEnd. Use YYYY-MM-DD." });
        if (end < start)
            return BadRequest(new AuthErrorResponse { Message = "periodEnd must be on or after periodStart." });

        var role = GetUserRole();
        var p = await db.Projects
            .AsNoTracking()
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == body.ProjectId, ct);
        if (p is null || p.Client is null || !p.IsActive)
            return NotFound(new AuthErrorResponse { Message = "Project not found or inactive." });
        if (!CanIssueInvoice(userId, role, p))
            return Forbid();

        var expenses = await LoadApprovedProjectExpensesAsync(p, start, end, ct);
        if (expenses.Count == 0)
            return BadRequest(new AuthErrorResponse { Message = "No approved expenses in this period for that project." });

        var byUser = expenses.GroupBy(e => e.UserId).OrderBy(g => g.Key).ToList();
        var now = DateTime.UtcNow;
        var responses = new List<IssueProjectInvoiceResponse>(byUser.Count);

        foreach (var g in byUser)
        {
            var issueNumber = NextIssueNumber();
            var inv = new IssuedInvoice
            {
                Id = Guid.NewGuid(),
                Kind = IssuedInvoiceKind.UserPayout,
                ProjectId = p.Id,
                PayeeUserId = g.Key,
                PeriodStart = start,
                PeriodEnd = end,
                IssueNumber = issueNumber,
                IssuedAtUtc = now,
                IssuedByUserId = userId,
                TotalAmount = g.Sum(x => x.Amount),
            };
            var order = 0;
            foreach (var e in g.OrderBy(x => x.ExpenseDate))
            {
                inv.Lines.Add(new IssuedInvoiceLine
                {
                    Id = Guid.NewGuid(),
                    ExpenseEntryId = e.Id,
                    Description = $"{e.ExpenseDate:yyyy-MM-dd} · {e.Category} · {e.Description}",
                    Amount = e.Amount,
                    SortOrder = order++,
                });
            }

            db.IssuedInvoices.Add(inv);
            responses.Add(new IssueProjectInvoiceResponse
            {
                InvoiceId = inv.Id,
                IssueNumber = inv.IssueNumber,
                TotalAmount = inv.TotalAmount,
                LineCount = inv.Lines.Count,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(new IssuePayoutInvoicesResponse { Invoices = responses });
    }

    [HttpGet("issued")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<ActionResult<IReadOnlyList<IssuedInvoiceListItemDto>>> ListIssued(
        [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var role = GetUserRole();

        IQueryable<IssuedInvoice> q = db.IssuedInvoices.AsNoTracking()
            .Include(i => i.Project!)
            .ThenInclude(p => p.Client)
            .Include(i => i.Payee)
            .OrderByDescending(i => i.IssuedAtUtc);

        if (projectId is { } pid)
            q = q.Where(i => i.ProjectId == pid);

        if (role == AppRole.Finance)
            q = q.Where(i => db.Projects.Any(p => p.Id == i.ProjectId && p.AssignedFinanceUserId == userId));

        var rows = await q.Take(200).ToListAsync(ct);
        return Ok(rows.Select(i => new IssuedInvoiceListItemDto
        {
            Id = i.Id,
            Kind = i.Kind.ToString(),
            ProjectId = i.ProjectId,
            ProjectName = i.Project?.Name ?? "",
            ClientName = i.Project?.Client?.Name ?? "",
            PayeeEmail = i.Payee?.Email,
            PeriodStart = i.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEnd = i.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IssueNumber = i.IssueNumber,
            IssuedAtUtc = i.IssuedAtUtc,
            TotalAmount = i.TotalAmount,
        }).ToList());
    }

    [HttpGet("{id:guid}/print")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var role = GetUserRole();

        var inv = await db.IssuedInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Project!)
            .ThenInclude(p => p.Client)
            .Include(i => i.Payee)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null)
            return NotFound();
        if (inv.Project is null)
            return NotFound();

        if (!CanIssueInvoice(userId, role, inv.Project))
            return Forbid();

        var html = BuildPrintHtml(inv);
        return Content(html, "text/html; charset=utf-8");
    }

    private static string NextIssueNumber() =>
        $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private async Task<List<ExpenseEntry>> LoadApprovedProjectExpensesAsync(
        Project p,
        DateOnly start,
        DateOnly end,
        CancellationToken ct)
    {
        var clientName = p.Client!.Name.ToLowerInvariant();
        var projectName = p.Name.ToLowerInvariant();
        return await db.ExpenseEntries.AsNoTracking()
            .Where(e =>
                e.Status == ExpenseStatus.Approved &&
                e.ExpenseDate >= start &&
                e.ExpenseDate <= end &&
                e.Client.ToLower() == clientName &&
                e.Project.ToLower() == projectName)
            .OrderBy(e => e.ExpenseDate)
            .ToListAsync(ct);
    }

    private static bool CanIssueInvoice(Guid userId, AppRole role, Project p)
    {
        if (role == AppRole.Admin) return true;
        if (role == AppRole.Finance) return p.AssignedFinanceUserId == userId;
        return false;
    }

    private static string BuildPrintHtml(IssuedInvoice inv)
    {
        var client = inv.Project?.Client?.Name ?? "";
        var project = inv.Project?.Name ?? "";
        var kindLabel = inv.Kind == IssuedInvoiceKind.UserPayout ? "Staff reimbursement summary" : "Project approved expenses";
        var payee = inv.Payee is null ? "" : $"<p><strong>Payee:</strong> {WebUtility.HtmlEncode(inv.Payee.DisplayName)} ({WebUtility.HtmlEncode(inv.Payee.Email)})</p>";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>")
            .Append(WebUtility.HtmlEncode(inv.IssueNumber))
            .Append("</title><style>")
            .Append("body{font-family:system-ui,Segoe UI,sans-serif;margin:2rem;color:#111}")
            .Append("h1{font-size:1.35rem}table{width:100%;border-collapse:collapse;margin-top:1rem}")
            .Append("th,td{border:1px solid #ccc;padding:.5rem .6rem;text-align:left}")
            .Append("th{background:#f4f4f4}.num{text-align:right}.foot td{font-weight:600;border:none}")
            .Append("@media print{body{margin:1cm}button{display:none}}")
            .Append("</style></head><body>")
            .Append("<p><button type=\"button\" onclick=\"window.print()\">Print / Save as PDF</button></p>")
            .Append("<h1>").Append(WebUtility.HtmlEncode(inv.IssueNumber)).Append("</h1>")
            .Append("<p><strong>").Append(WebUtility.HtmlEncode(kindLabel)).Append("</strong></p>")
            .Append("<p><strong>Client:</strong> ").Append(WebUtility.HtmlEncode(client))
            .Append(" &nbsp;|&nbsp; <strong>Project:</strong> ").Append(WebUtility.HtmlEncode(project)).Append("</p>")
            .Append("<p><strong>Period:</strong> ")
            .Append(WebUtility.HtmlEncode(inv.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Append(" → ")
            .Append(WebUtility.HtmlEncode(inv.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Append("</p>")
            .Append("<p><strong>Issued (UTC):</strong> ")
            .Append(WebUtility.HtmlEncode(inv.IssuedAtUtc.ToString("u", CultureInfo.InvariantCulture)))
            .Append("</p>")
            .Append(payee)
            .Append("<table><thead><tr><th>Description</th><th class=\"num\">Amount</th></tr></thead><tbody>");

        foreach (var line in inv.Lines.OrderBy(l => l.SortOrder))
        {
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(line.Description)).Append("</td><td class=\"num\">")
                .Append(line.Amount.ToString("C2", CultureInfo.InvariantCulture))
                .Append("</td></tr>");
        }

        sb.Append("</tbody><tfoot><tr class=\"foot\"><td>Total</td><td class=\"num\">")
            .Append(inv.TotalAmount.ToString("C2", CultureInfo.InvariantCulture))
            .Append("</td></tr></tfoot></table></body></html>");
        return sb.ToString();
    }

    private static bool TryParseDateOnly(string input, out DateOnly date) =>
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
