using System.Reflection;
using System.Text;
using C2E.Api.Data;
using C2E.Api.Options;
using Microsoft.Extensions.Hosting;

namespace C2E.Api.Configuration;

/// <summary>
/// Supplies configuration values read from <c>api/.env</c> so they override JSON, user secrets, environment variables, and command line for local runs.
/// </summary>
public static class DotEnvFilePriority
{
    /// <summary>Returns false only for xUnit (<c>testhost</c>). Otherwise <c>api/.env</c> is merged so local keys are not skipped when env is mis-labeled.</summary>
    public static bool ShouldApplyLocalDotEnvFile()
    {
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name ?? "";
        return !entryName.Contains("testhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Keys use <c>:</c> section syntax for <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// </summary>
    public static Dictionary<string, string?> BuildConfigurationOverrides(string apiContentRoot)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!ShouldApplyLocalDotEnvFile())
            return result;

        var map = MergeAllDotEnvKeyValues(apiContentRoot, hostEnv: null);

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
    /// Merges every discovered <c>.env</c> / <c>.env.local</c> in search order, then reapplies <paramref name="primaryApiContentRoot"/> and (when set) <paramref name="hostEnv"/>.<c>ContentRootPath</c> so the API project folder always wins.
    /// </summary>
    public static Dictionary<string, string> MergeAllDotEnvKeyValues(string? primaryApiContentRoot, IHostEnvironment? hostEnv)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateDotEnvFileCandidates(primaryApiContentRoot, hostEnv))
        {
            if (!File.Exists(path))
                continue;
            foreach (var kv in ParseDotEnvFile(path))
                merged[kv.Key] = kv.Value;
        }

        OverlayDirectoryDotEnv(merged, primaryApiContentRoot);
        if (hostEnv is not null)
            OverlayDirectoryDotEnv(merged, hostEnv.ContentRootPath?.Trim());

        return merged;
    }

    private static void OverlayDirectoryDotEnv(Dictionary<string, string> merged, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        // .env.local last so it wins over .env when the same key exists in both (e.g. empty OPENAI_API_KEY= in .env).
        foreach (var leaf in new[] { ".env", ".env.local" })
        {
            var p = Path.Combine(directory.Trim(), leaf);
            if (!File.Exists(p))
                continue;
            foreach (var kv in ParseDotEnvFile(p))
                merged[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Writes merged local dotenv into the process environment (overrides empty machine vars) so <see cref="Microsoft.Extensions.Configuration"/> and OpenAI clients see the same values as <see cref="ParseDotEnvFile"/>.
    /// </summary>
    public static void HydrateProcessEnvironmentFromDevelopmentDotEnvFiles(string apiContentRoot)
    {
        if (!ShouldApplyLocalDotEnvFile())
            return;

        var merged = MergeAllDotEnvKeyValues(apiContentRoot, hostEnv: null);
        foreach (var kv in merged)
        {
            var name = DotEnvKeyToProcessEnvironmentVariableName(kv.Key);
            Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim());
        }
    }

    private static string DotEnvKeyToProcessEnvironmentVariableName(string dotEnvKey) =>
        dotEnvKey.Contains(':') ? dotEnvKey.Replace(":", "__", StringComparison.Ordinal) : dotEnvKey;

    /// <summary>
    /// Reads <c>.env</c> from disk + process env into <paramref name="opts"/> (no testhost gate — caller decides).
    /// </summary>
    public static void MergeOpenAiFromDotEnvDisk(string apiContentRoot, AiRecommendationOptions opts)
    {
        ApplyOpenAiFromMergedMap(MergeAllDotEnvKeyValues(apiContentRoot, hostEnv: null), opts);
        ApplyOpenAiProcessEnvironmentFallback(opts);
    }

    private static void ApplyOpenAiFromMergedMap(Dictionary<string, string> merged, AiRecommendationOptions opts)
    {
        if (TryGetOpenAiApiKeyFromMap(merged, out var apiKey))
            opts.OpenAiApiKey = apiKey.Trim();

        if (TryGetAiProviderFromMap(merged, out var pv))
        {
            var p = pv.Trim().ToLowerInvariant();
            if (p is "openai" or "hybrid" or "deterministic")
                opts.Provider = pv.Trim();
        }
    }

    /// <summary>Loads OpenAI-related keys from one <c>.env</c> file (does not read process environment).</summary>
    public static void MergeOpenAiFromDotEnvFile(string absolutePath, AiRecommendationOptions opts)
    {
        if (!File.Exists(absolutePath))
            return;

        var map = ParseDotEnvFile(absolutePath);

        if (TryGetOpenAiApiKeyFromMap(map, out var apiKey))
            opts.OpenAiApiKey = apiKey.Trim();

        if (TryGetAiProviderFromMap(map, out var pv))
        {
            var p = pv.Trim().ToLowerInvariant();
            if (p is "openai" or "hybrid" or "deterministic")
                opts.Provider = pv.Trim();
        }
    }

    private static void ApplyOpenAiProcessEnvironmentFallback(AiRecommendationOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.OpenAiApiKey))
        {
            var fromEnv =
                Environment.GetEnvironmentVariable("AIRecommendations__OpenAiApiKey")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                opts.OpenAiApiKey = fromEnv.Trim();
        }

        if (!string.IsNullOrWhiteSpace(opts.OpenAiApiKey) &&
            string.Equals((opts.Provider ?? "").Trim(), "deterministic", StringComparison.OrdinalIgnoreCase))
            opts.Provider = "openai";
    }

    /// <summary>
    /// Forces <see cref="AiRecommendationOptions"/> from <c>api/.env</c> after configuration binding (covers binder/section edge cases).
    /// </summary>
    public static void ApplyOpenAiOptionsFromDotEnvFile(string apiContentRoot, AiRecommendationOptions opts)
    {
        if (!ShouldApplyLocalDotEnvFile())
            return;

        MergeOpenAiFromDotEnvDisk(apiContentRoot, opts);
    }

    private static bool TryGetOpenAiApiKeyFromMap(IReadOnlyDictionary<string, string> map, out string val)
    {
        foreach (var key in new[] { "AIRecommendations__OpenAiApiKey", "AIRecommendations:OpenAiApiKey", "OPENAI_API_KEY" })
        {
            if (TryNonEmpty(map, key, out val))
                return true;
        }

        val = "";
        return false;
    }

    private static bool TryGetAiProviderFromMap(IReadOnlyDictionary<string, string> map, out string val)
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
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Shell-style "export KEY=value"
            if (line.Length >= 7 && line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                line = line[7..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim().TrimStart('\uFEFF');
            if (k.Length == 0) continue;
            var v = line[(eq + 1)..].Trim();
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
                v = v[1..^1];
            else if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'')
                v = v[1..^1];
            else
                v = StripTrailingUnquotedHashComment(v);

            d[k] = v;
        }

        return d;
    }

    /// <summary># starts a comment unless inside single/double quotes (unquoted values only).</summary>
    private static string StripTrailingUnquotedHashComment(string v)
    {
        var inDouble = false;
        var inSingle = false;
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '#' && !inDouble && !inSingle)
                return v[..i].TrimEnd();
        }

        return v.TrimEnd();
    }

    private static bool TryNonEmpty(IReadOnlyDictionary<string, string> map, string key, out string val)
    {
        if (map.TryGetValue(key, out var got) && !string.IsNullOrWhiteSpace(got))
        {
            val = got;
            return true;
        }

        val = "";
        return false;
    }

    /// <summary>
    /// Finds the API project folder that contains <c>.env</c> (handles odd <see cref="IHostEnvironment.ContentRootPath"/> when running from <c>bin/</c>).
    /// </summary>
    public static string ResolveDotEnvBaseDirectory(IHostEnvironment env)
    {
        static bool HasEnv(string dir) =>
            File.Exists(Path.Combine(dir, ".env")) || File.Exists(Path.Combine(dir, ".env.local"));

        static bool HasEnvAndCsproj(string dir) => HasEnv(dir) && File.Exists(Path.Combine(dir, "C2E.Api.csproj"));

        var cr = env.ContentRootPath?.Trim() ?? "";
        if (cr.Length > 0 && HasEnv(cr))
            return cr;

        // Typical F5 / dotnet run: .../api/bin/Debug/net9.0 → .../api/.env
        try
        {
            var twoUpEnv = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".env"));
            var twoUpLocal = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".env.local"));
            if (File.Exists(twoUpEnv) || File.Exists(twoUpLocal))
                return Path.GetDirectoryName(twoUpEnv)!;
        }
        catch
        {
            // ignore
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (HasEnvAndCsproj(dir.FullName))
                return dir.FullName;

            var nestedApi = Path.Combine(dir.FullName, "api");
            if (HasEnvAndCsproj(nestedApi))
                return nestedApi;
        }

        if (cr.Length > 0)
            return cr;

        return Directory.GetCurrentDirectory();
    }

    /// <summary>Ordered roots to try for <c>.env</c> (first hit with a usable OpenAI key wins in <see cref="WithDotEnvOpenAiOverlay"/>).</summary>
    public static IEnumerable<string> EnumerateOpenAiDotEnvRoots(IHostEnvironment env)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in new[] { ResolveDotEnvBaseDirectory(env), env.ContentRootPath?.Trim() ?? "", Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            var full = Path.GetFullPath(d);
            if (!seen.Add(full)) continue;
            yield return full;
        }
    }

    /// <summary>First existing <c>.env</c> on disk from the same search order as runtime OpenAI merge.</summary>
    public static string? FindFirstExistingDotEnvPath(string? primaryApiContentRoot, IHostEnvironment? hostEnv)
    {
        foreach (var p in EnumerateDotEnvFileCandidates(primaryApiContentRoot, hostEnv))
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    /// <summary>
    /// Absolute paths to try for <c>.env</c> (deduped). Handles <c>dotnet run</c> from repo root, odd <see cref="IHostEnvironment.ContentRootPath"/>, and OneDrive layouts.
    /// </summary>
    public static IReadOnlyList<string> EnumerateDotEnvFileCandidates(string? primaryApiContentRoot, IHostEnvironment? hostEnv)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void addFile(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch
            {
                return;
            }

            if (seen.Add(full))
                list.Add(full);
        }

        void addPairForDirectory(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            addFile(Path.Combine(dir, ".env"));
            addFile(Path.Combine(dir, ".env.local"));
        }

        if (hostEnv is not null)
        {
            var resolved = Path.GetFullPath(ResolveDotEnvBaseDirectory(hostEnv));
            addPairForDirectory(resolved);
            var cr = hostEnv.ContentRootPath?.Trim();
            if (!string.IsNullOrWhiteSpace(cr))
            {
                var crFull = Path.GetFullPath(cr);
                if (!string.Equals(resolved, crFull, StringComparison.OrdinalIgnoreCase))
                    addPairForDirectory(crFull);
            }
        }

        if (!string.IsNullOrWhiteSpace(primaryApiContentRoot))
            addPairForDirectory(primaryApiContentRoot);

        // Prefer api/.env when running from repo root so a random parent .env is not picked first.
        addPairForDirectory(Path.Combine(Directory.GetCurrentDirectory(), "api"));
        addPairForDirectory(Directory.GetCurrentDirectory());

        try
        {
            var bd = AppContext.BaseDirectory;
            for (var depth = 1; depth <= 6; depth++)
            {
                var parts = new string[depth];
                Array.Fill(parts, "..");
                addPairForDirectory(Path.GetFullPath(Path.Combine(bd, Path.Combine(parts))));
            }
        }
        catch
        {
            // ignore
        }

        var asmLoc = typeof(DotEnvFilePriority).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(asmLoc))
        {
            try
            {
                for (var dir = new DirectoryInfo(Path.GetDirectoryName(asmLoc)!); dir is not null; dir = dir.Parent)
                {
                    addPairForDirectory(dir.FullName);
                    addPairForDirectory(Path.Combine(dir.FullName, "api"));
                }
            }
            catch
            {
                // ignore
            }
        }

        return list;
    }

    /// <summary>
    /// Copy of bound options with <c>api/.env</c> OpenAI fields applied — use for runtime LLM calls so options binding cannot drop the key.
    /// </summary>
    public static AiRecommendationOptions WithDotEnvOpenAiOverlay(IHostEnvironment env, AiRecommendationOptions bound)
    {
        var o = new AiRecommendationOptions
        {
            Provider = bound.Provider,
            OpenAiApiKey = bound.OpenAiApiKey,
            OpenAiBaseUrl = bound.OpenAiBaseUrl,
            OpenAiModel = bound.OpenAiModel,
            OpenAiTimeoutSeconds = bound.OpenAiTimeoutSeconds,
            OpenAiMaxCandidates = bound.OpenAiMaxCandidates,
            OpenAiTemperature = bound.OpenAiTemperature,
        };

        if (!ShouldApplyLocalDotEnvFile())
            return o;

        ApplyOpenAiFromMergedMap(MergeAllDotEnvKeyValues(primaryApiContentRoot: null, env), o);
        ApplyOpenAiProcessEnvironmentFallback(o);

        return o;
    }
}
