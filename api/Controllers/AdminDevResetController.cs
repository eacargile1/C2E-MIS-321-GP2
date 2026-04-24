using System.Security.Claims;
using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace C2E.Api.Controllers;

/// <summary>Local-only helpers for wiping test data before a demo.</summary>
[ApiController]
[Route("api/admin/dev")]
[Authorize(Roles = nameof(AppRole.Admin))]
public sealed class AdminDevResetController(
    AppDbContext db,
    IWebHostEnvironment env,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Deletes all clients, projects, assignments, timesheets, expenses, PTO, project tasks, quotes, user skills,
    /// and any users whose email is not one of the dev seed accounts (see README / <c>Seed:DevUserEmail</c> + *.dev@c2e.local).
    /// Only available when <c>ASPNETCORE_ENVIRONMENT</c> is Development.
    /// </summary>
    [HttpPost("clear-transactional-data")]
    public async Task<ActionResult<ClearTransactionalDataResponse>> ClearTransactionalData(CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var adminEmail = configuration["Seed:DevUserEmail"] ?? "dev@c2e.local";
        await TransactionalDemoReset.ClearAsync(db, adminEmail, ct);
        return Ok(new ClearTransactionalDataResponse
        {
            Message =
                "Removed clients, projects, assignments, timesheets, expenses, PTO, tasks, quotes, skills, and non–dev users. Dev seed accounts preserved.",
        });
    }

    /// <summary>True when the clear endpoint is available (Development + admin JWT).</summary>
    [HttpGet("clear-transactional-data")]
    public ActionResult<ClearTransactionalDataAvailabilityResponse> ClearTransactionalDataAvailability()
    {
        if (!env.IsDevelopment())
            return NotFound();
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name);
        return Ok(new ClearTransactionalDataAvailabilityResponse
        {
            Available = true,
            AuthenticatedUserId = sub,
        });
    }
}

public sealed record ClearTransactionalDataResponse
{
    public required string Message { get; init; }
}

public sealed record ClearTransactionalDataAvailabilityResponse
{
    public bool Available { get; init; }
    public string? AuthenticatedUserId { get; init; }
}
