using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using C2E.Api.Options;
using Microsoft.Extensions.Options;

namespace C2E.Api.Services;

public sealed record StaffingRerankCandidate(Guid UserId, string DisplayName, string Role, decimal TotalScore, List<string> Rationale);
public sealed record StaffingRerankResult(Guid UserId, string Rationale);

public interface IOpenAiStaffingReranker
{
    Task<IReadOnlyList<StaffingRerankResult>?> RerankAsync(
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<StaffingRerankCandidate> candidates,
        CancellationToken ct);
}

public sealed class OpenAiStaffingReranker(
    IHttpClientFactory httpClientFactory,
    IOptions<AiRecommendationOptions> opts,
    ILogger<OpenAiStaffingReranker> log) : IOpenAiStaffingReranker
{
    public async Task<IReadOnlyList<StaffingRerankResult>?> RerankAsync(
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<StaffingRerankCandidate> candidates,
        CancellationToken ct)
    {
        var cfg = opts.Value;
        var provider = cfg.Provider.Trim().ToLowerInvariant();
        if (provider is not ("openai" or "hybrid") || candidates.Count == 0 || string.IsNullOrWhiteSpace(cfg.OpenAiApiKey))
            return null;

        var top = candidates.Take(Math.Max(1, cfg.OpenAiMaxCandidates)).ToList();
        var prompt = BuildPrompt(requiredSkills, top);
        var request = new
        {
            model = cfg.OpenAiModel,
            temperature = cfg.OpenAiTemperature,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You rerank staffing candidates. Return strict JSON only."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        try
        {
            var http = httpClientFactory.CreateClient(nameof(OpenAiStaffingReranker));
            http.BaseAddress = new Uri(cfg.OpenAiBaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, cfg.OpenAiTimeoutSeconds));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.OpenAiApiKey.Trim());

            using var response = await http.PostAsJsonAsync("v1/chat/completions", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("OpenAI rerank returned non-success {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: ct);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var parsed = JsonSerializer.Deserialize<OpenAiRankingEnvelope>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed?.Rankings is null || parsed.Rankings.Count == 0)
                return null;

            return parsed.Rankings
                .Where(r => r.UserId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Rationale))
                .Select(r => new StaffingRerankResult(r.UserId, r.Rationale!.Trim()))
                .ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "OpenAI rerank failed.");
            return null;
        }
    }

    private static string BuildPrompt(IReadOnlyList<string> requiredSkills, IReadOnlyList<StaffingRerankCandidate> candidates)
    {
        var skills = requiredSkills.Count == 0 ? "(none provided)" : string.Join(", ", requiredSkills);
        var candidatesJson = JsonSerializer.Serialize(candidates);
        return
            "Required skills: " + skills + "\n\n" +
            "Candidates (already pre-scored): " + candidatesJson + "\n\n" +
            "Task:\n" +
            "1) Rerank candidates for best staffing fit.\n" +
            "2) Keep ranking mostly aligned with totalScore unless clear fit reason exists.\n" +
            "3) Return strict JSON with shape:\n" +
            "{\n" +
            "  \"rankings\": [\n" +
            "    { \"userId\": \"GUID\", \"rationale\": \"one sentence\" }\n" +
            "  ]\n" +
            "}\n" +
            "Do not include markdown or extra text.";
    }

    private sealed class OpenAiResponse
    {
        public List<OpenAiChoice> Choices { get; init; } = [];
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; init; }
    }

    private sealed class OpenAiMessage
    {
        public string? Content { get; init; }
    }

    private sealed class OpenAiRankingEnvelope
    {
        public List<OpenAiRankingRow> Rankings { get; init; } = [];
    }

    private sealed class OpenAiRankingRow
    {
        public Guid UserId { get; init; }
        public string? Rationale { get; init; }
    }
}
