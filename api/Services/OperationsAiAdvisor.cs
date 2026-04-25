using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using C2E.Api.Dtos;
using C2E.Api.Configuration;
using C2E.Api.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

public interface IOperationsAiAdvisor
{
    Task<OperationsExpenseAiReviewResponse> ReviewExpenseDraftAsync(
        OperationsExpenseAiReviewRequest req,
        CancellationToken ct,
        bool approverContext = false,
        string? submitterEmail = null);

    Task<OperationsTimesheetWeekAiReviewResponse> ReviewTimesheetWeekAsync(
        OperationsTimesheetWeekAiReviewRequest req,
        CancellationToken ct,
        bool approverContext = false,
        string? subjectEmail = null);
}

public sealed class OperationsAiAdvisor(
    IHttpClientFactory httpClientFactory,
    IOptions<AiRecommendationOptions> opts,
    IHostEnvironment env,
    ILogger<OperationsAiAdvisor> log) : IOperationsAiAdvisor
{
    public const string HttpClientName = nameof(OperationsAiAdvisor);
    private string? lastLlmDebug;

    public async Task<OperationsExpenseAiReviewResponse> ReviewExpenseDraftAsync(
        OperationsExpenseAiReviewRequest req,
        CancellationToken ct,
        bool approverContext = false,
        string? submitterEmail = null)
    {
        var cfg = DotEnvFilePriority.WithDotEnvOpenAiOverlay(env, opts.Value);
        var heuristics = BuildExpenseHeuristics(req, approverContext);
        var llmEnabled = LlmEnabled(cfg);
        lastLlmDebug = llmEnabled ? "start" : "disabled:no_key";
        var llm = llmEnabled ? await TryExpenseLlmLayerAsync(req, heuristics, cfg, ct, approverContext, submitterEmail) : null;
        return MergeExpense(heuristics, llm, llmEnabled, approverContext, submitterEmail, env.IsDevelopment() ? lastLlmDebug : null);
    }

    public async Task<OperationsTimesheetWeekAiReviewResponse> ReviewTimesheetWeekAsync(
        OperationsTimesheetWeekAiReviewRequest req,
        CancellationToken ct,
        bool approverContext = false,
        string? subjectEmail = null)
    {
        var cfg = DotEnvFilePriority.WithDotEnvOpenAiOverlay(env, opts.Value);
        var (total, heuristics) = BuildTimesheetHeuristics(req);
        var llmEnabled = LlmEnabled(cfg);
        lastLlmDebug = llmEnabled ? "start" : "disabled:no_key";
        var llm = llmEnabled
            ? await TryTimesheetLlmLayerAsync(req, total, heuristics, cfg, ct, approverContext, subjectEmail)
            : null;
        return MergeTimesheet(total, heuristics, llm, llmEnabled, approverContext, subjectEmail, env.IsDevelopment() ? lastLlmDebug : null);
    }

    private static bool LlmEnabled(AiRecommendationOptions cfg) =>
        !string.IsNullOrWhiteSpace((cfg.OpenAiApiKey ?? "").Trim());

    private static List<OperationsAiInsightDto> BuildExpenseHeuristics(OperationsExpenseAiReviewRequest req, bool approver)
    {
        var list = new List<OperationsAiInsightDto>();
        var desc = req.Description.Trim();
        var cat = req.Category.Trim();

        if (req.Amount >= 250m && !req.HasInvoiceAttachment)
        {
            list.Add(new OperationsAiInsightDto
            {
                Severity = "warn",
                Code = "receipt_gap",
                Message = approver
                    ? "$250+ with no receipt on file — consider requesting documentation before approving."
                    : "Amount is $250+ with no receipt attached — approvers often bounce these.",
                Source = "heuristic",
            });
        }

        if (req.Amount >= 500m && desc.Length < 20)
        {
            list.Add(new OperationsAiInsightDto
            {
                Severity = "risk",
                Code = "thin_narrative",
                Message = approver
                    ? "High amount with a very short business purpose — verify policy fit before approving."
                    : "High dollar amount but the business purpose is very short; add who/what/why.",
                Source = "heuristic",
            });
        }

        if (desc.Length > 0 && desc.Length < 12)
        {
            list.Add(new OperationsAiInsightDto
            {
                Severity = "warn",
                Code = "short_description",
                Message = approver
                    ? "Description is unusually short for an audit trail — consider asking for detail."
                    : "Description is unusually short for audit trail expectations.",
                Source = "heuristic",
            });
        }

        if (req.Amount >= 100m && req.Amount == decimal.Truncate(req.Amount))
        {
            list.Add(new OperationsAiInsightDto
            {
                Severity = "info",
                Code = "round_amount",
                Message = approver
                    ? "Round-dollar amount — confirm receipt or policy allows without backup."
                    : "Round-dollar charges are fine, but attach receipt/context if policy requires proof.",
                Source = "heuristic",
            });
        }

        if (LooksLikeTravel(cat, desc) && cat.Contains("meal", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new OperationsAiInsightDto
            {
                Severity = "info",
                Code = "category_travel_vs_meals",
                Message = "Category says Meals but description looks travel-related — confirm category matches policy.",
                Source = "heuristic",
            });
        }

        return list;
    }

    private static bool LooksLikeTravel(string category, string description)
    {
        var blob = $"{category} {description}".ToLowerInvariant();
        return blob.Contains("flight", StringComparison.Ordinal) ||
               blob.Contains("hotel", StringComparison.Ordinal) ||
               blob.Contains("uber", StringComparison.Ordinal) ||
               blob.Contains("lyft", StringComparison.Ordinal) ||
               blob.Contains("airline", StringComparison.Ordinal);
    }

    private async Task<ExpenseLlmEnvelope?> TryExpenseLlmLayerAsync(
        OperationsExpenseAiReviewRequest req,
        IReadOnlyList<OperationsAiInsightDto> heuristics,
        AiRecommendationOptions cfg,
        CancellationToken ct,
        bool approverContext,
        string? submitterEmail)
    {
        var heuristicCodes = string.Join(", ", heuristics.Select(h => h.Code).Distinct());
        var userJson = JsonSerializer.Serialize(new
        {
            submitterEmail,
            req.ExpenseDate,
            req.Client,
            req.Project,
            req.Category,
            req.Description,
            req.Amount,
            req.HasInvoiceAttachment,
            existingHeuristicCodes = heuristicCodes,
        });

        var roleLine = approverContext
            ? "You assist a Manager/Partner/Admin who is **approving** a pending expense. Questions should help the reviewer decide (not coaching the submitter directly).\n"
            : "You assist an employee reviewing their own draft expense before submit.\n";

        var prompt =
            roleLine +
            "Rules: use ONLY the JSON facts below; do not invent merchants, attendees, or policy text.\n" +
            "Return one JSON object only — no markdown fences, no prose before or after. Shape:\n" +
            "{\n" +
            "  \"additionalFlags\": [{\"severity\":\"info|warn|risk\",\"code\":\"snake_case\",\"message\":\"string\"}],\n" +
            "  \"questionsForSubmitter\": [\"string\"],\n" +
            "  \"briefSummary\": \"one sentence\"\n" +
            "}\n" +
            "Max 4 additionalFlags, max 3 questions. For approver mode, questionsForSubmitter are **reviewer checklist items**.\n" +
            "Avoid duplicating issues already covered by codes: " +
            heuristicCodes + "\n\n" +
            "Expense JSON:\n" + userJson;

        return await PostOpenAiJsonAsync<ExpenseLlmEnvelope>(prompt, cfg, ct);
    }

    private async Task<TimesheetLlmEnvelope?> TryTimesheetLlmLayerAsync(
        OperationsTimesheetWeekAiReviewRequest req,
        decimal weekTotal,
        IReadOnlyList<OperationsAiInsightDto> heuristics,
        AiRecommendationOptions cfg,
        CancellationToken ct,
        bool approverContext,
        string? subjectEmail)
    {
        var heuristicCodes = string.Join(", ", heuristics.Select(h => h.Code).Distinct());
        var lines = req.Lines.Select(l => new
        {
            l.WorkDate,
            l.Client,
            l.Project,
            l.Task,
            l.Hours,
            l.IsBillable,
            Notes = l.Notes?.Trim(),
        });
        var userJson = JsonSerializer.Serialize(new
        {
            subjectEmail,
            req.WeekStartMonday,
            weekTotalHours = weekTotal,
            lines,
            existingHeuristicCodes = heuristicCodes,
        });

        var roleLine = approverContext
            ? "You assist a Manager/Partner/Admin **approving someone else's submitted week**. questionsForEmployee should be **reviewer checklist** items (word them for the approver).\n"
            : "You assist weekly timesheet quality for consultants entering their own week.\n";

        var prompt =
            roleLine +
            "Rules: use ONLY the JSON facts; totals are authoritative from the payload.\n" +
            "Return one JSON object only — no markdown fences, no prose before or after. Shape:\n" +
            "{\n" +
            "  \"additionalFlags\": [{\"severity\":\"info|warn|risk\",\"code\":\"snake_case\",\"message\":\"string\"}],\n" +
            "  \"questionsForEmployee\": [\"string\"],\n" +
            "  \"noteSuggestions\": [\"short template; omit if approver mode unless helpful for feedback to employee\"],\n" +
            "  \"briefSummary\": \"one sentence\"\n" +
            "}\n" +
            "Max 4 additionalFlags, max 3 questions, max 3 noteSuggestions.\n" +
            "Do not duplicate heuristic codes: " + heuristicCodes + "\n\n" +
            "Timesheet JSON:\n" + userJson;

        return await PostOpenAiJsonAsync<TimesheetLlmEnvelope>(prompt, cfg, ct);
    }

    private async Task<T?> PostOpenAiJsonAsync<T>(string userPrompt, AiRecommendationOptions cfg, CancellationToken ct)
        where T : class
    {
        var messages = new object[]
        {
            new
            {
                role = "system",
                content =
                    "You output a single JSON object for internal ops tooling. " +
                    "Never wrap JSON in markdown code fences. Property names use camelCase.",
            },
            new { role = "user", content = userPrompt },
        };
        var withJson = new
        {
            model = cfg.OpenAiModel,
            temperature = cfg.OpenAiTemperature,
            response_format = new { type = "json_object" },
            messages,
        };
        var plain = new
        {
            model = cfg.OpenAiModel,
            temperature = cfg.OpenAiTemperature,
            messages,
        };

        try
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            var timeoutSec = Math.Max(30, cfg.OpenAiTimeoutSeconds * 5);
            OpenAiChatCompletionHelper.ConfigureClient(
                http,
                cfg.OpenAiBaseUrl,
                cfg.OpenAiApiKey,
                TimeSpan.FromSeconds(timeoutSec));
            var content = await OpenAiChatCompletionHelper.PostV1ForAssistantStringWithJsonObjectFallback(
                http, withJson, plain, log, "Operations AI", ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                lastLlmDebug = "extract:none";
                return null;
            }
            lastLlmDebug = "extract:ok";

            var parsed = LlmStructuredJsonHelper.Deserialize<T>(content);
            if (parsed is not null)
            {
                lastLlmDebug = "parse:deserialize";
                return parsed;
            }
            if (typeof(T) == typeof(ExpenseLlmEnvelope))
            {
                var ex = CoerceExpenseFromNode(content);
                if (ex is not null)
                {
                    lastLlmDebug = "parse:coerce_expense";
                    return (T?)(object?)ex;
                }
                var exFallback = FallbackExpenseFromRawText(content);
                if (exFallback is not null)
                {
                    lastLlmDebug = "parse:fallback_text_expense";
                    return (T?)(object?)exFallback;
                }
                lastLlmDebug = "parse:failed_expense";
                log.LogWarning(
                    "Operations AI: expense LLM JSON not usable (len {Len}, head: {Head})",
                    content.Length,
                    OpenAiChatCompletionHelper.TruncateForLog(content, 200));
                return null;
            }

            if (typeof(T) == typeof(TimesheetLlmEnvelope))
            {
                var ts = CoerceTimesheetFromNode(content);
                if (ts is not null)
                {
                    lastLlmDebug = "parse:coerce_timesheet";
                    return (T?)(object?)ts;
                }
                var tsFallback = FallbackTimesheetFromRawText(content);
                if (tsFallback is not null)
                {
                    lastLlmDebug = "parse:fallback_text_timesheet";
                    return (T?)(object?)tsFallback;
                }
                lastLlmDebug = "parse:failed_timesheet";
                log.LogWarning(
                    "Operations AI: timesheet LLM JSON not usable (len {Len}, head: {Head})",
                    content.Length,
                    OpenAiChatCompletionHelper.TruncateForLog(content, 200));
                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            lastLlmDebug = ex is TaskCanceledException ? "http:timeout_or_cancel" : "http:exception";
            log.LogWarning(ex, "Operations AI OpenAI call failed.");
            return null;
        }
    }

    private static void AppendDistinctLinesFromScalar(JsonObject o, List<string> lines, params string[] keys)
    {
        var s = LlmStructuredJsonHelper.FirstString(o, keys);
        if (string.IsNullOrWhiteSpace(s)) return;
        foreach (var part in s.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var t = part.Trim().TrimStart('•', '-', '*', ' ', '\t').Trim();
            if (t.Length == 0) continue;
            if (lines.Exists(x => string.Equals(x, t, StringComparison.Ordinal))) continue;
            lines.Add(t);
        }
    }

    private static List<string> ExtractTextBullets(string raw, int maxItems)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var part in raw.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var t = part.Trim().TrimStart('•', '-', '*', ' ', '\t').Trim();
            if (t.Length < 4) continue;
            if (list.Exists(x => string.Equals(x, t, StringComparison.Ordinal))) continue;
            list.Add(t);
            if (list.Count >= maxItems) break;
        }

        if (list.Count == 0)
        {
            var s = raw.Trim();
            if (s.Length > 0) list.Add(s.Length <= 320 ? s : s[..320] + "...");
        }

        return list;
    }

    private static ExpenseLlmEnvelope? FallbackExpenseFromRawText(string raw)
    {
        var lines = ExtractTextBullets(raw, 6);
        if (lines.Count == 0) return null;
        return new ExpenseLlmEnvelope
        {
            BriefSummary = lines[0],
            QuestionsForSubmitter = lines.Skip(1).Take(5).ToList(),
            AdditionalFlags = null,
        };
    }

    private static TimesheetLlmEnvelope? FallbackTimesheetFromRawText(string raw)
    {
        var lines = ExtractTextBullets(raw, 8);
        if (lines.Count == 0) return null;
        return new TimesheetLlmEnvelope
        {
            BriefSummary = lines[0],
            QuestionsForEmployee = lines.Skip(1).Take(4).ToList(),
            NoteSuggestions = lines.Skip(1).Take(4).ToList(),
            AdditionalFlags = null,
        };
    }

    private static ExpenseLlmEnvelope? CoerceExpenseFromNode(string raw)
    {
        var o = LlmStructuredJsonHelper.TryParseObject(raw);
        if (o is null) return null;
        var flags = ReadFlagRows(
            LlmStructuredJsonHelper.FirstArray(
                o,
                "additionalFlags",
                "additional_flags",
                "flags",
                "issues",
                "insights",
                "recommendations",
                "concerns",
                "findings"));
        var questions = new List<string>();
        var qArr = LlmStructuredJsonHelper.FirstArray(
            o,
            "questionsForSubmitter",
            "questions_for_submitter",
            "questions",
            "checklist",
            "followUps",
            "follow_ups");
        var fromArr = ReadStringList(qArr);
        if (fromArr is not null) questions.AddRange(fromArr);
        AppendDistinctLinesFromScalar(
            o,
            questions,
            "questionsForSubmitter",
            "questions_for_submitter",
            "questions",
            "checklist",
            "followUps",
            "follow_ups");
        var brief = LlmStructuredJsonHelper.FirstString(
            o,
            "briefSummary",
            "brief_summary",
            "summary",
            "overview",
            "memo",
            "narrative");
        if ((flags is null || flags.Count == 0) && questions.Count == 0 && brief is null)
            return null;
        return new ExpenseLlmEnvelope
        {
            AdditionalFlags = flags,
            QuestionsForSubmitter = questions.Count > 0 ? questions : null,
            BriefSummary = brief,
        };
    }

    private static TimesheetLlmEnvelope? CoerceTimesheetFromNode(string raw)
    {
        var o = LlmStructuredJsonHelper.TryParseObject(raw);
        if (o is null) return null;
        var flags = ReadFlagRows(
            LlmStructuredJsonHelper.FirstArray(
                o,
                "additionalFlags",
                "additional_flags",
                "flags",
                "issues",
                "insights",
                "recommendations",
                "concerns",
                "findings"));
        var questions = new List<string>();
        var qArr = LlmStructuredJsonHelper.FirstArray(
            o,
            "questionsForEmployee",
            "questions_for_employee",
            "questions",
            "reviewerChecklist",
            "followUps",
            "follow_ups");
        var qFromArr = ReadStringList(qArr);
        if (qFromArr is not null) questions.AddRange(qFromArr);
        AppendDistinctLinesFromScalar(
            o,
            questions,
            "questionsForEmployee",
            "questions_for_employee",
            "questions",
            "reviewerChecklist",
            "followUps",
            "follow_ups");
        var notes = new List<string>();
        var nArr = LlmStructuredJsonHelper.FirstArray(
            o,
            "noteSuggestions",
            "note_suggestions",
            "templates",
            "suggestedNotes");
        var nFromArr = ReadStringList(nArr);
        if (nFromArr is not null) notes.AddRange(nFromArr);
        AppendDistinctLinesFromScalar(o, notes, "noteSuggestions", "note_suggestions", "templates", "suggestedNotes");
        var brief = LlmStructuredJsonHelper.FirstString(
            o,
            "briefSummary",
            "brief_summary",
            "summary",
            "overview",
            "memo",
            "narrative");
        if ((flags is null || flags.Count == 0) &&
            questions.Count == 0 &&
            notes.Count == 0 &&
            brief is null)
            return null;
        return new TimesheetLlmEnvelope
        {
            AdditionalFlags = flags,
            QuestionsForEmployee = questions.Count > 0 ? questions : null,
            NoteSuggestions = notes.Count > 0 ? notes : null,
            BriefSummary = brief,
        };
    }

    private static List<LlmFlagRow>? ReadFlagRows(JsonArray? arr)
    {
        if (arr is null) return null;
        var list = new List<LlmFlagRow>();
        foreach (var item in arr)
        {
            if (item is JsonObject fo)
            {
                var msg = LlmStructuredJsonHelper.FirstString(
                    fo,
                    "message",
                    "text",
                    "detail",
                    "description",
                    "content",
                    "body",
                    "insight",
                    "finding",
                    "note",
                    "headline",
                    "title");
                if (string.IsNullOrWhiteSpace(msg)) continue;
                var code = LlmStructuredJsonHelper.FirstString(fo, "code", "id", "key") ?? "llm_flag";
                var sev = LlmStructuredJsonHelper.FirstString(fo, "severity", "level") ?? "info";
                list.Add(new LlmFlagRow { Code = code.Trim(), Message = msg.Trim(), Severity = sev.Trim() });
            }
            else if (item is JsonValue jv)
            {
                try
                {
                    var s = jv.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(new LlmFlagRow { Code = "llm_note", Message = s.Trim(), Severity = "info" });
                }
                catch (InvalidOperationException)
                {
                    // skip
                }
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static List<string>? ReadStringList(JsonArray? arr)
    {
        if (arr is null) return null;
        var list = new List<string>();
        foreach (var item in arr)
        {
            if (item is JsonValue v)
            {
                try
                {
                    var s = v.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                catch (InvalidOperationException)
                {
                    // skip
                }
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static OperationsExpenseAiReviewResponse MergeExpense(
        List<OperationsAiInsightDto> heuristics,
        ExpenseLlmEnvelope? llm,
        bool llmEnabled,
        bool approverContext,
        string? submitterEmail,
        string? llmDebug)
    {
        var questions = new List<string>();
        var insights = new List<OperationsAiInsightDto>(heuristics);
        var used = llm is not null;

        if (llm is not null)
        {
            foreach (var f in llm.AdditionalFlags ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Message) || string.IsNullOrWhiteSpace(f.Code)) continue;
                var sev = NormalizeSeverity(f.Severity);
                insights.Add(new OperationsAiInsightDto
                {
                    Severity = sev,
                    Code = f.Code.Trim(),
                    Message = f.Message.Trim(),
                    Source = "llm",
                });
            }

            foreach (var q in llm.QuestionsForSubmitter ?? [])
            {
                if (!string.IsNullOrWhiteSpace(q)) questions.Add(q.Trim());
            }

            if (!string.IsNullOrWhiteSpace(llm.BriefSummary))
                questions.Insert(0, llm.BriefSummary.Trim());
        }

        DeduplicateInsights(insights);
        return new OperationsExpenseAiReviewResponse
        {
            ReviewKind = approverContext ? "approver" : "draft",
            SubmitterEmail = submitterEmail,
            UsedLlm = used,
            LlmNote = used
                ? null
                : llmEnabled
                    ? "LLM was enabled but did not return usable output (timeout, HTTP error, or parse failure)."
                    : "LLM disabled — no OpenAiApiKey (set OPENAI_API_KEY or AIRecommendations__OpenAiApiKey in api/.env or environment).",
            LlmDebug = llmDebug,
            Insights = insights,
            QuestionsForSubmitter = questions.Distinct().Take(8).ToList(),
        };
    }

    private static OperationsTimesheetWeekAiReviewResponse MergeTimesheet(
        decimal weekTotal,
        List<OperationsAiInsightDto> heuristics,
        TimesheetLlmEnvelope? llm,
        bool llmEnabled,
        bool approverContext,
        string? subjectEmail,
        string? llmDebug)
    {
        var questions = new List<string>();
        var noteSuggestions = new List<string>();
        var insights = new List<OperationsAiInsightDto>(heuristics);
        var used = llm is not null;

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

            foreach (var q in llm.QuestionsForEmployee ?? [])
                if (!string.IsNullOrWhiteSpace(q)) questions.Add(q.Trim());

            foreach (var n in llm.NoteSuggestions ?? [])
                if (!string.IsNullOrWhiteSpace(n)) noteSuggestions.Add(n.Trim());

            if (!string.IsNullOrWhiteSpace(llm.BriefSummary))
                questions.Insert(0, llm.BriefSummary.Trim());
        }

        DeduplicateInsights(insights);
        return new OperationsTimesheetWeekAiReviewResponse
        {
            ReviewKind = approverContext ? "approver" : "draft",
            SubjectEmail = subjectEmail,
            UsedLlm = used,
            LlmNote = used
                ? null
                : llmEnabled
                    ? "LLM was enabled but did not return usable output (timeout, HTTP error, or parse failure)."
                    : "LLM disabled — no OpenAiApiKey (set OPENAI_API_KEY or AIRecommendations__OpenAiApiKey in api/.env or environment).",
            LlmDebug = llmDebug,
            WeekTotalHours = weekTotal,
            Insights = insights,
            QuestionsForEmployee = questions.Distinct().Take(8).ToList(),
            NoteSuggestions = noteSuggestions.Distinct().Take(6).ToList(),
        };
    }

    private static void DeduplicateInsights(List<OperationsAiInsightDto> insights)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = insights.Count - 1; i >= 0; i--)
        {
            var key = insights[i].Code + "|" + insights[i].Message;
            if (!seen.Add(key)) insights.RemoveAt(i);
        }
        insights.Sort((a, b) => SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity)));
    }

    private static int SeverityRank(string s) => s.ToLowerInvariant() switch
    {
        "risk" => 0,
        "warn" => 1,
        _ => 2,
    };

    private static string NormalizeSeverity(string? s)
    {
        var v = (s ?? "info").Trim().ToLowerInvariant();
        return v is "risk" or "warn" or "info" ? v : "info";
    }

    private static (decimal total, List<OperationsAiInsightDto> insights) BuildTimesheetHeuristics(
        OperationsTimesheetWeekAiReviewRequest req)
    {
        var insights = new List<OperationsAiInsightDto>();
        decimal total = 0;
        var perDay = new Dictionary<string, decimal>(StringComparer.Ordinal);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var sawDuplicateKey = false;
        foreach (var line in req.Lines)
        {
            var h = line.Hours;
            if (h <= 0) continue;
            total += h;
            var wd = line.WorkDate.Trim();
            if (wd.Length > 0)
                perDay[wd] = (perDay.TryGetValue(wd, out var x) ? x : 0m) + h;

            var ck =
                $"{wd}|{line.Client.Trim()}|{line.Project.Trim()}|{line.Task.Trim()}";
            if (keys.Contains(ck)) sawDuplicateKey = true;
            else keys.Add(ck);

            var notes = line.Notes?.Trim() ?? "";
            if (h >= 8m && notes.Length < 8)
            {
                insights.Add(new OperationsAiInsightDto
                {
                    Severity = "warn",
                    Code = "heavy_day_light_notes",
                    Message =
                        $"On {wd}, {h:0.##}h on {line.Client}/{line.Project} with very short notes — add deliverable context for reviewers.",
                    Source = "heuristic",
                });
            }
        }

        if (sawDuplicateKey)
        {
            insights.Add(new OperationsAiInsightDto
            {
                Severity = "risk",
                Code = "duplicate_line_key",
                Message =
                    "At least one duplicate row for the same date/client/project/task — consolidate before save to match server merge behavior.",
                Source = "heuristic",
            });
        }

        if (total > 60m)
        {
            insights.Add(new OperationsAiInsightDto
            {
                Severity = "risk",
                Code = "week_over_60",
                Message = $"Week totals {total.ToString("0.##", CultureInfo.InvariantCulture)}h — verify before submit.",
                Source = "heuristic",
            });
        }
        else if (total > 45m)
        {
            insights.Add(new OperationsAiInsightDto
            {
                Severity = "warn",
                Code = "week_over_45",
                Message = $"Week totals {total.ToString("0.##", CultureInfo.InvariantCulture)}h — confirm accuracy.",
                Source = "heuristic",
            });
        }

        foreach (var (day, sum) in perDay)
        {
            if (sum > 12m)
            {
                insights.Add(new OperationsAiInsightDto
                {
                    Severity = "risk",
                    Code = "day_over_12",
                    Message =
                        $"{day} totals {sum.ToString("0.##", CultureInfo.InvariantCulture)}h — exceeds a typical single-day cap.",
                    Source = "heuristic",
                });
            }
        }

        return (total, insights);
    }

    private sealed class ExpenseLlmEnvelope
    {
        public List<LlmFlagRow>? AdditionalFlags { get; init; }
        public List<string>? QuestionsForSubmitter { get; init; }
        public string? BriefSummary { get; init; }
    }

    private sealed class TimesheetLlmEnvelope
    {
        public List<LlmFlagRow>? AdditionalFlags { get; init; }
        public List<string>? QuestionsForEmployee { get; init; }
        public List<string>? NoteSuggestions { get; init; }
        public string? BriefSummary { get; init; }
    }

    private sealed class LlmFlagRow
    {
        public string? Severity { get; init; }
        public string? Code { get; init; }
        public string? Message { get; init; }
    }

}
