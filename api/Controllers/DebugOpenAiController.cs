using C2E.Api.Configuration;
using C2E.Api.Options;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/debug/openai")]
[AllowAnonymous]
public sealed class DebugOpenAiController(
    IHttpClientFactory httpClientFactory,
    IOptions<AiRecommendationOptions> opts,
    IHostEnvironment env) : ControllerBase
{
    public sealed class ProbeRequest
    {
        public string Prompt { get; init; } = "Return JSON object: {\"ok\":true,\"note\":\"probe\"}";
        public string? Model { get; init; }
        public decimal? Temperature { get; init; }
    }

    [HttpGet("probe")]
    public Task<ActionResult<object>> ProbeGet(CancellationToken ct) => Probe(null, ct);

    [HttpPost("probe")]
    public async Task<ActionResult<object>> Probe([FromBody] ProbeRequest? req, CancellationToken ct)
    {
        if (!env.IsDevelopment()) return NotFound();
        var merged = DotEnvFilePriority.WithDotEnvOpenAiOverlay(env, opts.Value);
        var apiKeyPresent = !string.IsNullOrWhiteSpace((merged.OpenAiApiKey ?? "").Trim());
        if (!apiKeyPresent)
            return Ok(new { ok = false, stage = "config", message = "OpenAiApiKey is empty after overlay." });

        var prompt = string.IsNullOrWhiteSpace(req?.Prompt)
            ? "Return JSON object: {\"ok\":true,\"note\":\"probe\"}"
            : req!.Prompt.Trim();
        var model = string.IsNullOrWhiteSpace(req?.Model) ? merged.OpenAiModel : req!.Model!.Trim();
        var temp = req?.Temperature ?? merged.OpenAiTemperature;
        var messages = new object[]
        {
            new
            {
                role = "system",
                content = "You are a diagnostics endpoint. Return one JSON object only, no markdown."
            },
            new { role = "user", content = prompt },
        };
        var withJson = new { model, temperature = temp, response_format = new { type = "json_object" }, messages };
        var plain = new { model, temperature = temp, messages };

        var http = httpClientFactory.CreateClient("DebugOpenAiController");
        OpenAiChatCompletionHelper.ConfigureClient(
            http,
            merged.OpenAiBaseUrl,
            merged.OpenAiApiKey ?? "",
            TimeSpan.FromSeconds(Math.Max(45, merged.OpenAiTimeoutSeconds * 5)));

        using var r1 = await http.PostAsJsonAsync("v1/chat/completions", withJson, ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);
        var retriedWithoutResponseFormat = false;
        HttpStatusCode? status2 = null;
        string? b2 = null;
        string rawBodyUsed;
        if (r1.StatusCode == HttpStatusCode.BadRequest &&
            OpenAiChatCompletionHelper.IsLikelyJsonObjectResponseFormatRejection(b1))
        {
            retriedWithoutResponseFormat = true;
            using var r2 = await http.PostAsJsonAsync("v1/chat/completions", plain, ct);
            b2 = await r2.Content.ReadAsStringAsync(ct);
            status2 = r2.StatusCode;
            rawBodyUsed = b2;
        }
        else
        {
            rawBodyUsed = b1;
        }

        LlmStructuredJsonHelper.TryGetAssistantTextFromOpenAiChatCompletion(
            rawBodyUsed,
            out var text,
            out var topErr,
            out var refusal);

        var parsedNode = LlmStructuredJsonHelper.TryParseObject(text);
        return Ok(new
        {
            ok = !string.IsNullOrWhiteSpace(text),
            model,
            baseUrl = merged.OpenAiBaseUrl,
            apiKeyPresent,
            assistantTextLength = text?.Length ?? 0,
            assistantTextHead = OpenAiChatCompletionHelper.TruncateForLog(text, 220),
            parsedAsJsonObject = parsedNode is not null,
            firstHttpStatus = (int)r1.StatusCode,
            firstBodyHead = OpenAiChatCompletionHelper.TruncateForLog(b1, 360),
            retriedWithoutResponseFormat,
            secondHttpStatus = status2 is null ? (int?)null : (int)status2,
            secondBodyHead = OpenAiChatCompletionHelper.TruncateForLog(b2, 360),
            topLevelError = topErr,
            refusal
        });
    }
}
