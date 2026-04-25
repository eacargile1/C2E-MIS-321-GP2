using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace C2E.Api.Services;

/// <summary>
/// Normalizes model output before <see cref="JsonSerializer.Deserialize{TValue}"/> so fenced / wrapped JSON does not break the pipeline.
/// </summary>
public static class LlmStructuredJsonHelper
{
    public static readonly JsonSerializerOptions RelaxedCamel = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private static readonly JsonSerializerOptions RelaxedSnake = new(RelaxedCamel)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Removes optional markdown fences and returns the substring from the first "{" to the last "}".
    /// </summary>
    public static string ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..].TrimStart();
            var close = s.LastIndexOf("```", StringComparison.Ordinal);
            if (close >= 0) s = s[..close].Trim();
        }

        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start) return s.Substring(start, end - start + 1);
        return s;
    }

    public static T? Deserialize<T>(string? raw)
        where T : class
    {
        var json = ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json)) return null;

        foreach (var opt in new[] { RelaxedCamel, RelaxedSnake })
        {
            try
            {
                var r = JsonSerializer.Deserialize<T>(json, opt);
                if (r is not null) return r;
            }
            catch (JsonException)
            {
                // try next policy / caller fallback
            }
        }

        return null;
    }

    public static JsonObject? TryParseObject(string? raw)
    {
        var json = ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonArray? FirstArray(JsonObject o, params string[] names)
    {
        foreach (var n in names)
        {
            if (o[n] is JsonArray a) return a;
        }

        return null;
    }

    public static string? FirstString(JsonObject o, params string[] names)
    {
        foreach (var n in names)
        {
            var node = o[n];
            if (node is null) continue;
            try
            {
                var s = node.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch (InvalidOperationException)
            {
                // not a string scalar
            }
        }

        return null;
    }

    public static decimal? FirstDecimal(JsonObject o, params string[] names)
    {
        foreach (var n in names)
        {
            var node = o[n];
            if (node is null) continue;
            try
            {
                if (node is JsonValue v)
                {
                    if (v.TryGetValue(out decimal d)) return d;
                    if (v.TryGetValue(out int i)) return i;
                    if (v.TryGetValue(out long l)) return l;
                    if (v.TryGetValue(out double dbl)) return (decimal)dbl;
                    if (v.TryGetValue(out string? s) && decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }
            catch (InvalidOperationException)
            {
                // ignore
            }
        }

        return null;
    }

    /// <summary>Backward-compatible: assistant text or null, no diagnostic outs.</summary>
    public static string? TryExtractOpenAiChatCompletionAssistantContent(string? responseBody)
    {
        if (!TryGetAssistantTextFromOpenAiChatCompletion(
                responseBody,
                out var content,
                out _,
                out _)) return null;
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    /// <summary>
    /// Reads OpenAI/compatible <c>chat/completions</c> responses: <c>choices[].message.content</c>,
    /// with case-insensitive property names, array or object content, and top-level <c>error</c> + <c>refusal</c> diagnostics.
    /// </summary>
    public static bool TryGetAssistantTextFromOpenAiChatCompletion(
        string? responseBody,
        out string? content,
        out string? topLevelErrorMessage,
        out string? refusalMessage)
    {
        content = null;
        topLevelErrorMessage = null;
        refusalMessage = null;
        if (string.IsNullOrWhiteSpace(responseBody)) return false;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && TryGetPropertyCI(root, "error", out var errObj)
                && errObj.ValueKind == JsonValueKind.Object
                && TryGetPropertyCI(errObj, "message", out var errMsg)
                && errMsg.ValueKind == JsonValueKind.String)
            {
                var em = errMsg.GetString();
                if (!string.IsNullOrEmpty(em)) topLevelErrorMessage = em;
            }

            if (!TryGetPropertyCI(root, "choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var choice in choices.EnumerateArray())
            {
                if (!TryGetPropertyCI(choice, "message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                if (TryGetPropertyCI(msg, "refusal", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    var rs = r.GetString();
                    if (!string.IsNullOrEmpty(rs)) refusalMessage = rs;
                }

                if (TryGetPropertyCI(msg, "parsed", out var parsed) && parsed.ValueKind == JsonValueKind.Object)
                {
                    var raw = parsed.GetRawText();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        content = raw;
                        return true;
                    }
                }

                if (!TryGetPropertyCI(msg, "content", out var contentNode)) continue;
                var extracted = ExtractMessageContentElement(contentNode);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    content = extracted;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryGetPropertyCI(JsonElement o, string name, out JsonElement v)
    {
        v = default;
        if (o.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in o.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            v = p.Value;
            return true;
        }

        return false;
    }

    private static string? ExtractMessageContentElement(JsonElement contentNode)
    {
        return contentNode.ValueKind switch
        {
            JsonValueKind.String => contentNode.GetString(),
            JsonValueKind.Number => contentNode.GetRawText(), // e.g. rare numeric-as-text
            JsonValueKind.Array => JoinContentArray(contentNode),
            JsonValueKind.Object => ExtractObjectAsAssistantText(contentNode),
            _ => null,
        };
    }

    private static string? JoinContentArray(JsonElement array)
    {
        var sb = new StringBuilder();
        foreach (var part in array.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String) sb.Append(part.GetString());
            else if (TryGetPropertyCI(part, "type", out var t) && t.ValueKind == JsonValueKind.String
                     && t.GetString() is "text" or "input_text" or "output_text")
            {
                if (TryGetPropertyCI(part, "text", out var te) && te.ValueKind == JsonValueKind.String)
                    sb.Append(te.GetString());
            }
            else
            {
                if (TryGetPropertyCI(part, "text", out var tx) && tx.ValueKind == JsonValueKind.String) sb.Append(tx.GetString());
                else if (TryGetPropertyCI(part, "content", out var c) && c.ValueKind == JsonValueKind.String)
                    sb.Append(c.GetString());
            }
        }

        var joined = sb.ToString();
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? ExtractObjectAsAssistantText(JsonElement o)
    {
        if (TryGetPropertyCI(o, "text", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
        if (TryGetPropertyCI(o, "value", out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        if (TryGetPropertyCI(o, "string", out var s) && s.ValueKind == JsonValueKind.String) return s.GetString();
        // Some providers wrap output JSON as a single object; surface raw for downstream ExtractJsonObject.
        return o.GetRawText();
    }
}
