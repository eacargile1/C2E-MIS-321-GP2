namespace C2E.Api.Data;

/// <summary>Stable markers for demo scenario clients and seeded rows (purge + idempotency).</summary>
public static class DemoScenarioMarkers
{
    /// <summary>All packaged demo clients must start with this prefix so <see cref="DevelopmentDataPurge"/> can keep them.</summary>
    public const string ClientNamePrefix = "DEMO SCENARIO";

    /// <summary>Embeddable in PTO reasons (and similar) so purge can retain seeded demo PTO only.</summary>
    public const string PtoReasonMarker = "[demo-scenario]";

    public static bool IsDemoScenarioClient(string name) =>
        name.StartsWith(ClientNamePrefix, StringComparison.Ordinal);

    public static string ExpenseMarker(string scenarioId) => $"[demo-scenario:{scenarioId}]";

    /// <summary>Demo-only employee accounts (kept by <see cref="DevelopmentDataPurge"/>).</summary>
    public const string DemoEmployeeEmailDomain = "@employees.demo.c2e.local";

    public static bool IsDemoEmployeeEmail(string email) =>
        email.EndsWith(DemoEmployeeEmailDomain, StringComparison.OrdinalIgnoreCase);
}
