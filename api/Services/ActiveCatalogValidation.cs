using C2E.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Services;

/// <summary>
/// When the org has at least one active client, timesheet/expense client+project strings must match the directory (case-insensitive).
/// </summary>
public static class ActiveCatalogValidation
{
    public static async Task EnsureActiveClientAndProjectAsync(
        AppDbContext db,
        string clientName,
        string projectName,
        CancellationToken ct)
    {
        if (!await db.Clients.AnyAsync(c => c.IsActive, ct))
            return;

        var client = await db.Clients
            .AsNoTracking()
            .Where(c => c.IsActive && c.Name.ToLower() == clientName.ToLowerInvariant())
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(ct);

        if (client is null)
            throw new InvalidOperationException(
                $"Unknown or inactive client \"{clientName}\". Use an active client from the directory.");

        var projectOk = await db.Projects.AnyAsync(
            p => p.IsActive &&
                 p.ClientId == client.Id &&
                 p.Name.ToLower() == projectName.ToLowerInvariant(),
            ct);

        if (!projectOk)
            throw new InvalidOperationException(
                $"Unknown or inactive project \"{projectName}\" for client \"{clientName}\".");
    }
}
