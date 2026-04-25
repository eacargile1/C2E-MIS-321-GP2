using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace C2E.Api.Services;

/// <summary>
/// POST <c>v1/chat/completions</c> with optional retry when <c>response_format: json_object</c> is rejected (some models / proxies).
/// </summary>
public static class OpenAiChatCompletionHelper
{
    public static string TruncateForLog(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= max ? t : t[..max] + "…";
    }

    public static bool IsLikelyJsonObjectResponseFormatRejection(string body) =>
        body.Contains("response_format", StringComparison.OrdinalIgnoreCase)
        || (body.Contains("json_object", StringComparison.OrdinalIgnoreCase) &&
            (body.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("Invalid", StringComparison.Ordinal) ||
             body.Contains("invalid", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// For requests that include <c>response_format: { type: json_object }</c>: retry once without it on 400.
    /// </summary>
    public static async Task<string?> PostV1ForAssistantStringWithJsonObjectFallback(
        HttpClient http,
        object requestWithResponseJson,
        object requestPlain,
        ILogger? log,
        string logScope,
        CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("v1/chat/completions", requestWithResponseJson, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.BadRequest
            && IsLikelyJsonObjectResponseFormatRejection(body))
        {
            log?.LogWarning(
                "{Scope}: 400, retrying without response_format. Head: {Head}",
                logScope,
                TruncateForLog(body, 280));
            response = await http.PostAsJsonAsync("v1/chat/completions", requestPlain, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            log?.LogWarning(
                "{Scope}: OpenAI HTTP {Code}. Head: {Head}",
                logScope,
                (int)response.StatusCode,
                TruncateForLog(body, 420));
            return null;
        }

        LlmStructuredJsonHelper.TryGetAssistantTextFromOpenAiChatCompletion(
            body,
            out var text,
            out var topErr,
            out var refus);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        log?.LogWarning(
            "{Scope}: no assistant text. err={Err} refusal={Ref} bodyLen={Len} head={Head}",
            logScope,
            topErr,
            refus,
            body.Length,
            TruncateForLog(body, 360));
        return null;
    }

    public static async Task<string?> PostV1ForAssistantString(
        HttpClient http,
        object request,
        ILogger? log,
        string logScope,
        CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("v1/chat/completions", request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            log?.LogWarning(
                "{Scope}: OpenAI HTTP {Code}. Head: {Head}",
                logScope,
                (int)response.StatusCode,
                TruncateForLog(body, 420));
            return null;
        }

        LlmStructuredJsonHelper.TryGetAssistantTextFromOpenAiChatCompletion(
            body,
            out var text,
            out var topErr,
            out var refus);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        log?.LogWarning(
            "{Scope}: no assistant text. err={Err} refusal={Ref} bodyLen={Len} head={Head}",
            logScope,
            topErr,
            refus,
            body.Length,
            TruncateForLog(body, 360));
        return null;
    }

    public static void ConfigureClient(HttpClient http, string openAiBaseUrl, string apiKey, TimeSpan timeout)
    {
        http.BaseAddress = new Uri(openAiBaseUrl.TrimEnd('/') + "/");
        http.Timeout = timeout;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (apiKey ?? "").Trim());
    }
}
