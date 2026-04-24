using System.Reflection;
using C2E.Api.Data;
using C2E.Api.Options;

namespace C2E.Api.Configuration;

/// <summary>
/// Supplies configuration values read from <c>api/.env</c> so they override JSON, user secrets, environment variables, and command line for local runs.
/// </summary>
public static class DotEnvFilePriority
{
    public static bool ShouldApplyLocalDotEnvFile()
    {
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name ?? "";
        if (entryName.Contains("testhost", StringComparison.OrdinalIgnoreCase))
            return false;

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Keys use <c>:</c> section syntax for <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// </summary>
    public static Dictionary<string, string?> BuildConfigurationOverrides(string apiContentRoot)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!ShouldApplyLocalDotEnvFile())
            return result;

        var path = Path.Combine(apiContentRoot, ".env");
        if (!File.Exists(path))
            return result;

        var map = ParseDotEnvFile(path);

        if (TryNonEmpty(map, "ConnectionStrings__DefaultConnection", out var cs))
            result["ConnectionStrings:DefaultConnection"] = cs.Trim();
        else if (TryNonEmpty(map, "DATABASE_URL", out var du))
            result["ConnectionStrings:DefaultConnection"] = HerokuDatabaseUrl.ToMySqlConnectionString(du.Trim());

        if (TryGetOpenAiApiKeyFromMap(map, out var apiKey))
            result["AIRecommendations:OpenAiApiKey"] = apiKey.Trim();

        if (TryGetAiProviderFromMap(map, out var pv))
        {
            var p = pv.Trim().ToLowerInvariant();
            if (p is "openai" or "hybrid" or "deterministic")
                result["AIRecommendations:Provider"] = pv.Trim();
        }

        return result;
    }

    /// <summary>
    /// Forces <see cref="AiRecommendationOptions"/> from <c>api/.env</c> after configuration binding (covers binder/section edge cases).
    /// </summary>
    public static void ApplyOpenAiOptionsFromDotEnvFile(string apiContentRoot, AiRecommendationOptions opts)
    {
        if (!ShouldApplyLocalDotEnvFile())
            return;

        var path = Path.Combine(apiContentRoot, ".env");
        if (!File.Exists(path))
            return;

        var map = ParseDotEnvFile(path);

        if (TryGetOpenAiApiKeyFromMap(map, out var apiKey))
            opts.OpenAiApiKey = apiKey.Trim();

        if (TryGetAiProviderFromMap(map, out var pv))
        {
            var p = pv.Trim().ToLowerInvariant();
            if (p is "openai" or "hybrid" or "deterministic")
                opts.Provider = pv.Trim();
        }
    }

    private static bool TryGetOpenAiApiKeyFromMap(Dictionary<string, string> map, out string val)
    {
        foreach (var key in new[] { "AIRecommendations__OpenAiApiKey", "AIRecommendations:OpenAiApiKey", "OPENAI_API_KEY" })
        {
            if (TryNonEmpty(map, key, out val))
                return true;
        }

        val = "";
        return false;
    }

    private static bool TryGetAiProviderFromMap(Dictionary<string, string> map, out string val)
    {
        foreach (var key in new[] { "AIRecommendations__Provider", "AIRecommendations:Provider" })
        {
            if (TryNonEmpty(map, key, out val))
                return true;
        }

        val = "";
        return false;
    }

    private static Dictionary<string, string> ParseDotEnvFile(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim().TrimStart('\uFEFF');
            if (k.Length == 0) continue;
            var v = line[(eq + 1)..].Trim();
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
                v = v[1..^1];
            d[k] = v;
        }

        return d;
    }

    private static bool TryNonEmpty(Dictionary<string, string> map, string key, out string val)
    {
        if (map.TryGetValue(key, out var got) && !string.IsNullOrWhiteSpace(got))
        {
            val = got;
            return true;
        }

        val = "";
        return false;
    }
}
