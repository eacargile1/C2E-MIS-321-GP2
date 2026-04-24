using System.Globalization;
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
}
