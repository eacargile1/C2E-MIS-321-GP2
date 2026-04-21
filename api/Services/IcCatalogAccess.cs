using C2E.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Services;

/// <summary>
/// IC visibility and booking rules: client- or project-level staffing assignments (client roster, project roster, or team member).
/// </summary>
public static class IcCatalogAccess
{
    public static async Task<HashSet<Guid>> GetAllowedClientIdsAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var fromClient = await db.ClientEmployeeAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.ClientId)
            .ToListAsync(ct);

        var fromPea = await (
            from a in db.ProjectEmployeeAssignments.AsNoTracking()
            join p in db.Projects.AsNoTracking() on a.ProjectId equals p.Id
            where a.UserId == userId
            select p.ClientId).Distinct().ToListAsync(ct);

        var fromTeam = await (
            from t in db.ProjectTeamMembers.AsNoTracking()
            join p in db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.UserId == userId
            select p.ClientId).Distinct().ToListAsync(ct);

        return new HashSet<Guid>(fromClient.Concat(fromPea).Concat(fromTeam));
    }

    public static async Task<HashSet<Guid>> GetAllowedProjectIdsAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var clientIds = await db.ClientEmployeeAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.ClientId)
            .ToListAsync(ct);

        var fromClientScope = await db.Projects.AsNoTracking()
            .Where(p => p.IsActive && clientIds.Contains(p.ClientId))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var fromPea = await db.ProjectEmployeeAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.ProjectId)
            .ToListAsync(ct);

        var fromTeam = await db.ProjectTeamMembers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.ProjectId)
            .ToListAsync(ct);

        return new HashSet<Guid>(fromClientScope.Concat(fromPea).Concat(fromTeam));
    }

    /// <summary>IC may log time/expenses against this catalog client+project pair.</summary>
    public static async Task<bool> MayUseClientProjectAsync(
        AppDbContext db,
        Guid userId,
        string clientName,
        string projectName,
        CancellationToken ct)
    {
        var projectId = await (
            from p in db.Projects.AsNoTracking()
            join c in db.Clients.AsNoTracking() on p.ClientId equals c.Id
            where p.IsActive && c.IsActive &&
                  c.Name.ToLower() == clientName.Trim().ToLowerInvariant() &&
                  p.Name.ToLower() == projectName.Trim().ToLowerInvariant()
            select p.Id).FirstOrDefaultAsync(ct);

        if (projectId == Guid.Empty)
            return false;

        var allowed = await GetAllowedProjectIdsAsync(db, userId, ct);
        return allowed.Contains(projectId);
    }
}
