using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class TimesheetCatalogValidationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"timesheet-catalog-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "admin@local.test");
            b.UseSetting("Seed:DevUserPassword", "AdminPass!9");
        });

    private static async Task<string> LoginTokenAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
    }

    private static async Task<string> AdminTokenAsync(HttpClient client) =>
        await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");

    [Fact]
    public async Task Put_week_rejects_unknown_project_when_catalog_exists()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var clientRes = await client.PostAsJsonAsync("/api/clients", new { name = "Acme Corp" });
        clientRes.EnsureSuccessStatusCode();
        var createdClient = await clientRes.Content.ReadFromJsonAsync<ClientDto>(JsonOpts);
        var projRes = await client.PostAsJsonAsync(
            "/api/projects",
            new { name = "Redesign", clientId = createdClient!.Id, budgetAmount = 10000m });
        projRes.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        var icToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        const string weekStart = "2026-04-06";
        var put = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme Corp",
                    project = "Does Not Exist",
                    task = "Build",
                    hours = 2m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_week_accepts_matching_client_and_project_when_catalog_exists()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var clientRes = await client.PostAsJsonAsync("/api/clients", new { name = "Globex" });
        clientRes.EnsureSuccessStatusCode();
        var createdClient = await clientRes.Content.ReadFromJsonAsync<ClientDto>(JsonOpts);
        var projRes = await client.PostAsJsonAsync(
            "/api/projects",
            new { name = "Portal", clientId = createdClient!.Id, budgetAmount = 5000m });
        projRes.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        var icToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        const string weekStart = "2026-04-06";
        var put = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Globex",
                    project = "Portal",
                    task = "Build",
                    hours = 2m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record ClientDto(Guid Id, string Name);
}
