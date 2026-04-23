using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

public interface IFinanceAiAssistant
{
    Task<FinanceLedgerAuditResponse> AuditLedgerAsync(AppDbContext db, FinanceLedgerAuditRequest req, CancellationToken ct);
    Task<FinanceQuoteDraftResponse> DraftQuoteAsync(AppDbContext db, FinanceQuoteDraftRequest req, CancellationToken ct);
}

public sealed class FinanceAiAssistant(
    IHttpClientFactory httpClientFactory,
    IOptions<AiRecommendationOptions> opts,
    ILogger<FinanceAiAssistant> log) : IFinanceAiAssistant
{
    public const string HttpClientName = nameof(FinanceAiAssistant);

    public async Task<FinanceLedgerAuditResponse> AuditLedgerAsync(
        AppDbContext db,
        FinanceLedgerAuditRequest req,
        CancellationToken ct)
    {
        var maxRows = Math.Clamp(req.MaxRows, 1, 200);
        var q = db.ExpenseEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(req.EmployeeEmailContains))
        {
            var needle = req.EmployeeEmailContains.Trim();
            q = q.Where(x => db.Users.Any(u => u.Id == x.UserId && u.Email.Contains(needle)));
        }

        if (!string.IsNullOrWhiteSpace(req.ClientNameContains))
        {
            var cn = req.ClientNameContains.Trim();
            q = q.Where(x => x.Client.Contains(cn));
        }

        var rows = await q
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(maxRows)
            .Join(db.Users.AsNoTracking(), e => e.UserId, u => u.Id, (e, u) => new { e, u.Email })
            .ToListAsync(ct);

        var insights = new List<OperationsAiInsightDto>();
        decimal pendingAmt = 0;
        decimal approvedAmt = 0;
        var pendingNoInv = 0;
        foreach (var x in rows)
        {
            if (x.e.Status == ExpenseStatus.Pending)
            {
                pendingAmt += x.e.Amount;
                if (x.e.InvoiceBytes is null or { Length: 0 }) pendingNoInv++;
            }
            else if (x.e.Status == ExpenseStatus.Approved) approvedAmt += x.e.Amount;
        }

        if (pendingNoInv > 0)
        {
            insights.Add(new OperationsAiInsightDto
            {
                Severity = pendingNoInv >= 5 ? "warn" : "info",
                Code = "pending_missing_receipt",
                Message = $"{pendingNoInv} pending row(s) in this slice have no receipt attachment.",
                Source = "heuristic",
            });
        }

        if (rows.Count > 0)
        {
            var top = rows.OrderByDescending(x => x.e.Amount).First();
            if (top.e.Status == ExpenseStatus.Pending && top.e.Amount >= 750m)
            {
                insights.Add(new OperationsAiInsightDto
                {
                    Severity = "warn",
                    Code = "large_pending_line",
                    Message =
                        $"Largest pending line is {top.e.Amount.ToString("0.##", CultureInfo.InvariantCulture)} for {top.Email} — confirm before period close.",
                    Source = "heuristic",
                });
            }
        }

        var llmEnabled = LlmEnabled();
        LedgerLlmEnvelope? llm = null;
        if (llmEnabled)
        {
            var slice = rows.Take(60).Select(x => new
            {
                x.Email,
                x.e.ExpenseDate,
                x.e.Client,
                x.e.Project,
                x.e.Category,
                x.e.Description,
                x.e.Amount,
                hasInvoice = x.e.InvoiceBytes is { Length: > 0 },
                status = x.e.Status.ToString(),
            });
            var payload = JsonSerializer.Serialize(new
            {
                rowCount = rows.Count,
                pendingTotal = pendingAmt,
                approvedTotal = approvedAmt,
                rows = slice,
            });
            var prompt =
                "You assist Finance reviewing an expense register slice for a professional services firm.\n" +
                "Use ONLY JSON facts. Return strict JSON (no markdown):\n" +
                "{\n" +
                "  \"additionalFlags\": [{\"severity\":\"info|warn|risk\",\"code\":\"snake_case\",\"message\":\"string\"}],\n" +
                "  \"summaryPoints\": [\"string\"]\n" +
                "}\n" +
                "Max 5 flags and 6 summary points. Do not invent dollar amounts; totals are in the JSON.\n\n" +
                payload;
            llm = await PostOpenAiJsonAsync<LedgerLlmEnvelope>(prompt, ct);
        }

        var summary = new List<string>();
        if (llm is not null)
        {
            foreach (var f in llm.AdditionalFlags ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Message) || string.IsNullOrWhiteSpace(f.Code)) continue;
                insights.Add(new OperationsAiInsightDto
                {
                    Severity = NormalizeSeverity(f.Severity),
                    Code = f.Code.Trim(),
                    Message = f.Message.Trim(),
                    Source = "llm",
                });
            }

            foreach (var s in llm.SummaryPoints ?? [])
                if (!string.IsNullOrWhiteSpace(s)) summary.Add(s.Trim());
        }

        if (summary.Count == 0)
        {
            summary.Add(
                $"Slice: {rows.Count} rows · pending ${pendingAmt:0.##} · approved ${approvedAmt:0.##} (heuristics + optional LLM).");
        }

        DeduplicateInsights(insights);
        var used = llm is not null;
        return new FinanceLedgerAuditResponse
        {
            UsedLlm = used,
            LlmNote = used
                ? null
                : llmEnabled
                    ? "LLM enabled but response unusable."
                    : "LLM off — set AIRecommendations:Provider to openai or hybrid and OpenAiApiKey.",
            RowCount = rows.Count,
            TotalPendingAmount = pendingAmt,
            TotalApprovedAmount = approvedAmt,
            Insights = insights,
            SummaryPoints = summary.Distinct().Take(10).ToList(),
        };
    }

    public async Task<FinanceQuoteDraftResponse> DraftQuoteAsync(
        AppDbContext db,
        FinanceQuoteDraftRequest req,
        CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.ClientId, ct);
        if (client is null)
        {
            return new FinanceQuoteDraftResponse
            {
                UsedLlm = false,
                LlmNote = "Client not found.",
                ReviewerChecklist = ["Fix client selection and try again."],
            };
        }

        var recentQuotes = await db.ClientQuotes.AsNoTracking()
            .Where(q => q.ClientId == req.ClientId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .Take(5)
            .Select(q => new { q.Title, q.ScopeSummary, q.EstimatedHours, q.HourlyRate, q.TotalAmount, q.Status })
            .ToListAsync(ct);

        var ledgerForClient = await db.ExpenseEntries.AsNoTracking()
            .Where(e => e.Client == client.Name)
            .OrderByDescending(e => e.ExpenseDate)
            .Take(25)
            .Join(db.Users.AsNoTracking(), e => e.UserId, u => u.Id, (e, u) => new { e.Amount, e.Status, e.Description, u.Email })
            .ToListAsync(ct);

        object? employeeContext = null;
        if (!string.IsNullOrWhiteSpace(req.ContextEmployeeEmail))
        {
            var em = req.ContextEmployeeEmail.Trim();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == em, ct);
            if (user is not null)
            {
                var userExp = ledgerForClient.Where(x =>
                    string.Equals(x.Email, em, StringComparison.OrdinalIgnoreCase)).ToList();
                employeeContext = new
                {
                    email = em,
                    note = $"{userExp.Count} expense lines for this client in recent pull (subset).",
                };
            }
        }

        var checklist = new List<string>
        {
            "Client default rate (if any) is a hint only — confirm active SOW.",
            "Compare hours to recent quotes for this client below.",
        };

        var llmEnabled = LlmEnabled();
        QuoteLlmEnvelope? llm = null;
        if (llmEnabled)
        {
            var payload = JsonSerializer.Serialize(new
            {
                clientName = client.Name,
                defaultHourlyRate = client.DefaultBillingRate,
                recentQuotes,
                recentExpenseSubset = ledgerForClient.Take(12),
                employeeContext,
            });
            var prompt =
                "You help Finance draft a client quote (estimated hours × hourly rate).\n" +
                "Use ONLY JSON facts. Return strict JSON (no markdown):\n" +
                "{\n" +
                "  \"suggestedTitle\": \"string\",\n" +
                "  \"suggestedScopeSummary\": \"string\",\n" +
                "  \"suggestedHours\": number,\n" +
                "  \"suggestedHourlyRate\": number,\n" +
                "  \"suggestedValidThroughYmd\": \"YYYY-MM-DD or empty\",\n" +
                "  \"reviewerChecklist\": [\"string\"]\n" +
                "}\n" +
                "Hours and rate must be plausible from the facts; if uncertain, bias toward client.defaultHourlyRate and recentQuotes medians.\n\n" +
                payload;
            llm = await PostOpenAiJsonAsync<QuoteLlmEnvelope>(prompt, ct);
        }

        var used = llm is not null;
        if (!used && client.DefaultBillingRate is decimal d)
        {
            checklist.Insert(0, $"Suggest starting from default billing rate {d.ToString("0.##", CultureInfo.InvariantCulture)}/hr.");
        }

        if (llm?.ReviewerChecklist is not null)
        {
            foreach (var c in llm.ReviewerChecklist)
                if (!string.IsNullOrWhiteSpace(c)) checklist.Add(c.Trim());
        }

        return new FinanceQuoteDraftResponse
        {
            UsedLlm = used,
            LlmNote = used
                ? null
                : llmEnabled
                    ? "LLM enabled but response unusable."
                    : "LLM off — heuristics/checklist only.",
            SuggestedTitle = llm?.SuggestedTitle?.Trim(),
            SuggestedScopeSummary = llm?.SuggestedScopeSummary?.Trim(),
            SuggestedHours = llm?.SuggestedHours is > 0 ? llm.SuggestedHours : null,
            SuggestedHourlyRate = llm?.SuggestedHourlyRate is > 0 ? llm.SuggestedHourlyRate : null,
            SuggestedValidThroughYmd = string.IsNullOrWhiteSpace(llm?.SuggestedValidThroughYmd)
                ? null
                : llm.SuggestedValidThroughYmd.Trim(),
            ReviewerChecklist = checklist.Distinct().Take(12).ToList(),
        };
    }

    private bool LlmEnabled()
    {
        var cfg = opts.Value;
        var p = cfg.Provider.Trim().ToLowerInvariant();
        return p is "openai" or "hybrid" && !string.IsNullOrWhiteSpace(cfg.OpenAiApiKey);
    }

    private async Task<T?> PostOpenAiJsonAsync<T>(string userPrompt, CancellationToken ct)
        where T : class
    {
        var cfg = opts.Value;
        var request = new
        {
            model = cfg.OpenAiModel,
            temperature = cfg.OpenAiTemperature,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "You output strict JSON only for internal ops tooling." },
                new { role = "user", content = userPrompt },
            },
        };

        try
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            http.BaseAddress = new Uri(cfg.OpenAiBaseUrl.TrimEnd('/') + "/");
            var timeoutSec = Math.Max(10, cfg.OpenAiTimeoutSeconds * 4);
            http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.OpenAiApiKey.Trim());

            using var response = await http.PostAsJsonAsync("v1/chat/completions", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Finance AI OpenAI HTTP {Status}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content)) return null;
            return JsonSerializer.Deserialize<T>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Finance AI OpenAI call failed.");
            return null;
        }
    }

    private static void DeduplicateInsights(List<OperationsAiInsightDto> insights)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = insights.Count - 1; i >= 0; i--)
        {
            var key = insights[i].Code + "|" + insights[i].Message;
            if (!seen.Add(key)) insights.RemoveAt(i);
        }
    }

    private static string NormalizeSeverity(string? s)
    {
        var v = (s ?? "info").Trim().ToLowerInvariant();
        return v is "risk" or "warn" or "info" ? v : "info";
    }

    private sealed class LedgerLlmEnvelope
    {
        public List<LlmFlagRow>? AdditionalFlags { get; init; }
        public List<string>? SummaryPoints { get; init; }
    }

    private sealed class LlmFlagRow
    {
        public string? Severity { get; init; }
        public string? Code { get; init; }
        public string? Message { get; init; }
    }

    private sealed class QuoteLlmEnvelope
    {
        public string? SuggestedTitle { get; init; }
        public string? SuggestedScopeSummary { get; init; }
        public decimal? SuggestedHours { get; init; }
        public decimal? SuggestedHourlyRate { get; init; }
        public string? SuggestedValidThroughYmd { get; init; }
        public List<string>? ReviewerChecklist { get; init; }
    }

    private sealed class OpenAiChatResponse
    {
        public List<OpenAiChoiceRow> Choices { get; init; } = [];
    }

    private sealed class OpenAiChoiceRow
    {
        public OpenAiMsg? Message { get; init; }
    }

    private sealed class OpenAiMsg
    {
        public string? Content { get; init; }
    }
}
