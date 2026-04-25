using System.Globalization;
using System.Text.Json;
using C2E.Api.Configuration;
using C2E.Api.Data;
using C2E.Api.Models;
using C2E.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

public interface IFinanceExpenseAiNarrativeService
{
    Task<(string Narrative, string Source)> BuildNarrativeAsync(
        Guid projectId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct);
}

/// <summary>
/// Produces a short finance-facing narrative from approved expense aggregates.
/// Uses OpenAI when configured; otherwise a deterministic heuristic (still useful for demos).
/// </summary>
public sealed class FinanceExpenseAiNarrativeService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<AiRecommendationOptions> opts,
    IHostEnvironment hostEnv,
    ILogger<FinanceExpenseAiNarrativeService> log) : IFinanceExpenseAiNarrativeService
{
    public async Task<(string Narrative, string Source)> BuildNarrativeAsync(
        Guid projectId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking()
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (p?.Client is null)
            return ("Project not found.", "heuristic");

        var clientNorm = p.Client.Name.ToLowerInvariant();
        var projectNorm = p.Name.ToLowerInvariant();

        var rows = await db.ExpenseEntries.AsNoTracking()
            .Where(e =>
                e.Status == ExpenseStatus.Approved &&
                e.ExpenseDate >= periodStart &&
                e.ExpenseDate <= periodEnd &&
                e.Client.ToLower() == clientNorm &&
                e.Project.ToLower() == projectNorm)
            .Select(e => new { e.Category, e.Amount, e.UserId })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return ("No approved expenses in this window to summarize.", "heuristic");

        var byCat = rows.GroupBy(r => r.Category)
            .Select(g => (Cat: g.Key, Sum: g.Sum(x => x.Amount), Cnt: g.Count()))
            .OrderByDescending(x => x.Sum)
            .ToList();
        var total = rows.Sum(x => x.Amount);
        var submitters = rows.Select(x => x.UserId).Distinct().Count();

        var cfg = DotEnvFilePriority.WithDotEnvOpenAiOverlay(hostEnv, opts.Value);
        if (string.IsNullOrWhiteSpace((cfg.OpenAiApiKey ?? "").Trim()))
            return (
                HeuristicNarrative(
                    p.Client.Name,
                    p.Name,
                    periodStart,
                    periodEnd,
                    total,
                    submitters,
                    byCat,
                    HeuristicNarrativeHint.NoApiKey),
                "heuristic");

        try
        {
            var summaryPayload = new
            {
                client = p.Client.Name,
                project = p.Name,
                periodStart = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                periodEnd = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                approvedExpenseCount = rows.Count,
                distinctSubmitters = submitters,
                totalUsd = total,
                byCategory = byCat.Select(x => new { category = x.Cat, lineCount = x.Cnt, amountUsd = x.Sum }).ToList(),
            };
            var json = JsonSerializer.Serialize(summaryPayload);

            var request = new
            {
                model = cfg.OpenAiModel,
                temperature = cfg.OpenAiTemperature,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "You assist finance analysts. Write 3-5 concise sentences for an internal finance memo. " +
                            "No markdown. No bullet characters. Focus on spend concentration, reimbursement risk signals (e.g. few categories dominating), and anything notable about volume vs submitters. " +
                            "Do not invent facts beyond the JSON. USD amounts are approximate aggregates only."
                    },
                    new { role = "user", content = "Expense aggregates JSON:\n" + json }
                }
            };

            var http = httpClientFactory.CreateClient(nameof(FinanceExpenseAiNarrativeService));
            OpenAiChatCompletionHelper.ConfigureClient(
                http,
                cfg.OpenAiBaseUrl,
                (cfg.OpenAiApiKey ?? "").Trim(),
                TimeSpan.FromSeconds(Math.Max(45, cfg.OpenAiTimeoutSeconds * 4)));

            var text = (await OpenAiChatCompletionHelper.PostV1ForAssistantString(
                http, request, log, "Finance AI narrative", ct))?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return (
                    HeuristicNarrative(
                        p.Client.Name,
                        p.Name,
                        periodStart,
                        periodEnd,
                        total,
                        submitters,
                        byCat,
                        HeuristicNarrativeHint.OpenAiFailed),
                    "heuristic");

            return (text, "openai");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "OpenAI narrative failed; using heuristic.");
            return (
                HeuristicNarrative(
                    p.Client.Name,
                    p.Name,
                    periodStart,
                    periodEnd,
                    total,
                    submitters,
                    byCat,
                    HeuristicNarrativeHint.OpenAiFailed),
                "heuristic");
        }
    }

    private enum HeuristicNarrativeHint
    {
        None,
        NoApiKey,
        OpenAiFailed,
    }

    private static string HeuristicNarrative(
        string clientName,
        string projectName,
        DateOnly start,
        DateOnly end,
        decimal total,
        int submitters,
        IReadOnlyList<(string Cat, decimal Sum, int Cnt)> byCat,
        HeuristicNarrativeHint hint = HeuristicNarrativeHint.None)
    {
        var top = byCat.Count == 0
            ? "n/a"
            : string.Join(
                "; ",
                byCat.Take(3).Select(x => $"{x.Cat} {x.Sum:C0} ({x.Cnt} lines)"));
        var core =
            $"Approved spend on {clientName} / {projectName} from {start:yyyy-MM-dd} through {end:yyyy-MM-dd} totals {total:C}. " +
            $"{submitters} distinct submitter(s) appear in this slice. " +
            $"Largest category buckets: {top}.";
        var tail = hint switch
        {
            HeuristicNarrativeHint.NoApiKey =>
                " Heuristic summary only — add OPENAI_API_KEY or AIRecommendations__OpenAiApiKey to api/.env (saved to disk) for OpenAI prose.",
            HeuristicNarrativeHint.OpenAiFailed =>
                " Heuristic summary only — OpenAI did not return usable prose for this request.",
            _ => "",
        };
        return core + tail;
    }
}
