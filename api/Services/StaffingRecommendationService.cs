using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

public interface IStaffingRecommendationService
{
    Task<StaffingRecommendationResponseDto> RecommendForProjectAsync(
        Guid projectId,
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct);
}

public sealed class StaffingRecommendationService(
    AppDbContext db,
    IOpenAiStaffingReranker openAiReranker,
    IOptions<AiRecommendationOptions> opts,
    ILogger<StaffingRecommendationService> log)
    : IStaffingRecommendationService
{
    public async Task<StaffingRecommendationResponseDto> RecommendForProjectAsync(
        Guid projectId,
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct)
    {
        try
        {
            if (requiredSkills.Any(s => string.Equals(s?.Trim(), "__force_error", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Forced recommendation failure for resilience testing.");
            return await RecommendInternalAsync(projectId, requiredSkills, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Staffing recommendation scoring failed; using system fallback.");
            var fallbackRows = await BuildFallbackAsync(projectId, requiredSkills, "system_fallback", ct);
            return new StaffingRecommendationResponseDto
            {
                FallbackMode = "system_fallback",
                Results = fallbackRows,
                WarningMessage = "Recommendation service fallback applied.",
            };
        }
    }

    private async Task<StaffingRecommendationResponseDto> RecommendInternalAsync(
        Guid projectId,
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new StaffingRecommendationResponseDto { FallbackMode = "availability_only", Results = [] };

        var normalizedSkills = requiredSkills
            .Select(NormalizeSkill)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidates = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);
        var userSkills = await db.UserSkills
            .AsNoTracking()
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Skills = g.Select(x => x.SkillName).ToList() })
            .ToDictionaryAsync(x => x.UserId, x => x.Skills, ct);

        var assignedUserIds = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.UserId)
            .ToHashSetAsync(ct);

        var now = DateTime.UtcNow;
        var prior90 = now.AddDays(-90);

        var assignmentCounts = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var recentProjectLoads = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.AssignedAtUtc >= prior90)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var utilizationHours = await db.TimesheetLines
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsBillable && t.WorkDate >= DateOnly.FromDateTime(prior90))
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Hours = g.Sum(x => x.Hours) })
            .ToDictionaryAsync(x => x.UserId, x => x.Hours, ct);

        var rows = new List<StaffingRecommendationResultDto>(candidates.Count);
        var hasAnySkillData = userSkills.Count > 0;

        foreach (var user in candidates)
        {
            var rationale = new List<string>();
            var fallbackReason = default(string);
            var completedAssignments = assignmentCounts.GetValueOrDefault(user.Id, 0);
            var recentAssignments = recentProjectLoads.GetValueOrDefault(user.Id, 0);
            var recentHours = utilizationHours.GetValueOrDefault(user.Id, 0m);
            var availabilityScore = AvailabilityFromLoad(recentAssignments, assignedUserIds.Contains(user.Id));

            var inferredSkills = InferSkills(user, userSkills.GetValueOrDefault(user.Id));
            var skillMatches = normalizedSkills.Length == 0
                ? 0
                : normalizedSkills.Count(req => inferredSkills.Contains(req));
            var skillScore = normalizedSkills.Length == 0 ? 0m : Math.Round((decimal)skillMatches / normalizedSkills.Length, 4);

            var utilizationScore = UtilizationScoreFromHours(recentHours);
            var isSparseHistory = completedAssignments < 3;

            decimal totalScore;
            if (isSparseHistory)
            {
                totalScore = availabilityScore;
                skillScore = 0m;
                utilizationScore = 0m;
                fallbackReason = "insufficient_history";
                rationale.Add("Fell back to availability-only due to sparse assignment history.");
            }
            else
            {
                totalScore = Math.Round((skillScore * 0.50m) + (availabilityScore * 0.30m) + (utilizationScore * 0.20m), 4);
            }

            rationale.Add($"Recent assignments (90d): {recentAssignments}.");
            rationale.Add($"Recent billable hours (90d): {recentHours:0.##}.");
            if (normalizedSkills.Length > 0 && !isSparseHistory)
                rationale.Add($"Skill matches: {skillMatches}/{normalizedSkills.Length}.");

            rows.Add(new StaffingRecommendationResultDto
            {
                UserId = user.Id,
                Email = user.Email,
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? UserProfileName.DefaultFromEmail(user.Email) : user.DisplayName,
                Role = user.Role.ToString(),
                TotalScore = totalScore,
                SkillScore = skillScore,
                AvailabilityScore = availabilityScore,
                UtilizationScore = utilizationScore,
                Rationale = rationale,
                FallbackReason = fallbackReason,
            });
        }

        var warning = normalizedSkills.Length > 0 && !hasAnySkillData
            ? "No candidate skill data detected; ranking is based on availability/utilization."
            : null;

        var baseResponse = new StaffingRecommendationResponseDto
        {
            FallbackMode = rows.Any(r => r.FallbackReason is "insufficient_history")
                ? "availability_only"
                : "none",
            Results = rows
                .OrderByDescending(r => r.TotalScore)
                .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WarningMessage = warning,
        };

        var provider = opts.Value.Provider.Trim().ToLowerInvariant();
        if (provider is not ("openai" or "hybrid"))
            return baseResponse;

        var rerank = await openAiReranker.RerankAsync(
            normalizedSkills,
            baseResponse.Results
                .Select(r => new StaffingRerankCandidate(r.UserId, r.DisplayName, r.Role, r.TotalScore, r.Rationale))
                .ToList(),
            ct);
        if (rerank is null || rerank.Count == 0)
            return baseResponse;

        var rankLookup = rerank
            .Select((r, i) => new { r.UserId, Index = i, r.Rationale })
            .ToDictionary(x => x.UserId, x => (x.Index, x.Rationale));

        var reranked = baseResponse.Results
            .OrderBy(r => rankLookup.TryGetValue(r.UserId, out var v) ? v.Index : int.MaxValue)
            .ThenByDescending(r => r.TotalScore)
            .Select(r =>
            {
                if (!rankLookup.TryGetValue(r.UserId, out var v))
                    return r;
                var rationale = new List<string>(r.Rationale)
                {
                    $"OpenAI rerank: {v.Rationale}"
                };
                return new StaffingRecommendationResultDto
                {
                    UserId = r.UserId,
                    Email = r.Email,
                    DisplayName = r.DisplayName,
                    Role = r.Role,
                    TotalScore = r.TotalScore,
                    SkillScore = r.SkillScore,
                    AvailabilityScore = r.AvailabilityScore,
                    UtilizationScore = r.UtilizationScore,
                    Rationale = rationale,
                    FallbackReason = r.FallbackReason,
                };
            })
            .ToList();

        return new StaffingRecommendationResponseDto
        {
            FallbackMode = baseResponse.FallbackMode is "none" ? "openai_hybrid" : baseResponse.FallbackMode,
            Results = reranked,
            WarningMessage = baseResponse.WarningMessage,
        };
    }

    private async Task<List<StaffingRecommendationResultDto>> BuildFallbackAsync(
        Guid projectId,
        IReadOnlyList<string> requiredSkills,
        string reason,
        CancellationToken ct)
    {
        var assignedUserIds = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.UserId)
            .ToHashSetAsync(ct);
        var prior90 = DateTime.UtcNow.AddDays(-90);
        var recentProjectLoads = await db.ProjectEmployeeAssignments
            .AsNoTracking()
            .Where(a => a.AssignedAtUtc >= prior90)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);
        return users
            .Select(u =>
            {
                var availability = AvailabilityFromLoad(recentProjectLoads.GetValueOrDefault(u.Id, 0), assignedUserIds.Contains(u.Id));
                return new StaffingRecommendationResultDto
                {
                    UserId = u.Id,
                    Email = u.Email,
                    DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? UserProfileName.DefaultFromEmail(u.Email) : u.DisplayName,
                    Role = u.Role.ToString(),
                    TotalScore = availability,
                    SkillScore = 0m,
                    AvailabilityScore = availability,
                    UtilizationScore = 0m,
                    Rationale = [$"System fallback ranking applied ({reason})."],
                    FallbackReason = reason,
                };
            })
            .OrderByDescending(r => r.TotalScore)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static decimal AvailabilityFromLoad(int recentAssignments, bool assignedOnCurrentProject)
    {
        var baseScore = 1m - Math.Min(recentAssignments, 5) / 5m;
        if (assignedOnCurrentProject)
            baseScore -= 0.2m;
        return Math.Clamp(Math.Round(baseScore, 4), 0m, 1m);
    }

    private static decimal UtilizationScoreFromHours(decimal hours)
    {
        var normalized = 1m - Math.Min(hours, 480m) / 480m;
        return Math.Clamp(Math.Round(normalized, 4), 0m, 1m);
    }

    private static HashSet<string> InferSkills(AppUser user, List<string>? explicitSkills)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in explicitSkills ?? [])
            set.Add(NormalizeSkill(skill));
        if (set.Count == 0)
        {
            foreach (var token in user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(NormalizeSkill(token));
        }
        set.Add(NormalizeSkill(user.Role.ToString()));
        return set.Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSkill(string raw) =>
        raw.Trim().ToLowerInvariant();
}
