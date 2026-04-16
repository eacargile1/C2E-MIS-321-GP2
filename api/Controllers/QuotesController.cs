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
[Route("api/quotes")]
[Authorize(Roles = RbacRoleSets.AdminAndFinance)]
public sealed class QuotesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuoteResponse>>> List(CancellationToken ct)
    {
        var rows = await db.ClientQuotes
            .AsNoTracking()
            .Include(q => q.Client)
            .OrderByDescending(q => q.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<QuoteResponse>> Create([FromBody] CreateQuoteRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == body.ClientId, ct);
        if (client is null)
            return BadRequest(new AuthErrorResponse { Message = "Client not found." });

        if (!string.IsNullOrWhiteSpace(body.Status) && ParseQuoteStatus(body.Status) is null)
            return BadRequest(new AuthErrorResponse { Message = "Invalid status. Use Draft or Sent." });
        var status = ParseQuoteStatus(body.Status) ?? QuoteStatus.Draft;
        DateOnly? validThrough = null;
        if (!string.IsNullOrWhiteSpace(body.ValidThrough))
        {
            if (!DateOnly.TryParseExact(
                    body.ValidThrough.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var vd))
                return BadRequest(new AuthErrorResponse { Message = "Invalid validThrough. Use YYYY-MM-DD." });
            validThrough = vd;
        }

        var total = Math.Round(body.EstimatedHours * body.HourlyRate, 2, MidpointRounding.AwayFromZero);
        var now = DateTime.UtcNow;
        var prefix = $"Q-{now:yyyyMM}-";
        var seq = await db.ClientQuotes.CountAsync(q => q.ReferenceNumber.StartsWith(prefix), ct) + 1;
        var reference = $"{prefix}{seq:D4}";

        Guid? createdBy = null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name);
        if (sub is not null && Guid.TryParse(sub, out var uid))
            createdBy = uid;

        var entity = new ClientQuote
        {
            Id = Guid.NewGuid(),
            ClientId = body.ClientId,
            ReferenceNumber = reference,
            Title = body.Title.Trim(),
            ScopeSummary = string.IsNullOrWhiteSpace(body.ScopeSummary) ? null : body.ScopeSummary.Trim(),
            EstimatedHours = body.EstimatedHours,
            HourlyRate = body.HourlyRate,
            TotalAmount = total,
            Status = status,
            ValidThrough = validThrough,
            CreatedByUserId = createdBy,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ClientQuotes.Add(entity);
        await db.SaveChangesAsync(ct);

        entity.Client = client;
        return Ok(Map(entity));
    }

    private static QuoteResponse Map(ClientQuote q)
    {
        var clientName = q.Client?.Name ?? "";
        return new QuoteResponse
        {
            Id = q.Id,
            ClientId = q.ClientId,
            ClientName = clientName,
            ReferenceNumber = q.ReferenceNumber,
            Title = q.Title,
            ScopeSummary = q.ScopeSummary,
            EstimatedHours = q.EstimatedHours,
            HourlyRate = q.HourlyRate,
            TotalAmount = q.TotalAmount,
            Status = q.Status.ToString(),
            ValidThrough = q.ValidThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CreatedAtUtc = q.CreatedAtUtc,
        };
    }

    private static QuoteStatus? ParseQuoteStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<QuoteStatus>(raw.Trim(), ignoreCase: true, out var s) ? s : null;
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
