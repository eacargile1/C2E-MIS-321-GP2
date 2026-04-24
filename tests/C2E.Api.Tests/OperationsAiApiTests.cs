using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class OperationsAiApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"ops-ai-tests-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "admin@local.test");
            b.UseSetting("Seed:DevUserPassword", "AdminPass!9");
            b.UseSetting("AIRecommendations:Provider", "deterministic");
        });

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.test", password = "AdminPass!9" });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
    }

    [Fact]
    public async Task Expense_review_requires_auth()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync(
            "/api/ai/operations/expense-review",
            new
            {
                expenseDate = "2026-04-06",
                client = "Acme",
                project = "P1",
                category = "Meals",
                description = "x",
                amount = 300m,
                hasInvoiceAttachment = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Expense_review_returns_heuristics_when_llm_disabled()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var res = await client.PostAsJsonAsync(
            "/api/ai/operations/expense-review",
            new
            {
                expenseDate = "2026-04-06",
                client = "Acme",
                project = "P1",
                category = "Meals",
                description = "short",
                amount = 300m,
                hasInvoiceAttachment = false,
            });
        res.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("usedLlm").GetBoolean());
        Assert.True(root.GetProperty("insights").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Timesheet_review_rejects_non_monday_week_start()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var res = await client.PostAsJsonAsync(
            "/api/ai/operations/timesheet-week-review",
            new
            {
                weekStartMonday = "2026-04-07",
                lines = Array.Empty<object>(),
            });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Timesheet_review_accepts_empty_week()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var res = await client.PostAsJsonAsync(
            "/api/ai/operations/timesheet-week-review",
            new { weekStartMonday = "2026-04-06", lines = Array.Empty<object>() });
        res.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal(0m, doc.RootElement.GetProperty("weekTotalHours").GetDecimal());
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);
}
