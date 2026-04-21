using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class AssignmentsApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"assignments-tests-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "admin@local.test");
            b.UseSetting("Seed:DevUserPassword", "AdminPass!9");
        });

    [Fact]
    public async Task Recommendations_return_ranked_scores_for_project()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "Recommendations Client" });
        createdClient.EnsureSuccessStatusCode();
        var clientBody = await createdClient.Content.ReadFromJsonAsync<ClientDto>();

        var createdProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Recommendations Project",
            clientId = clientBody!.Id,
            budgetAmount = 1200m,
        });
        createdProject.EnsureSuccessStatusCode();
        var project = await createdProject.Content.ReadFromJsonAsync<ProjectDto>();

        var res = await client.PostAsJsonAsync(
            $"/api/assignments/projects/{project!.Id}/recommendations",
            new { requiredSkills = new[] { "admin", "manager" } });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<RecommendationResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Results);
        Assert.All(body.Results, r =>
        {
            Assert.InRange(r.TotalScore, 0m, 1m);
            Assert.NotEmpty(r.Rationale);
        });
    }

    [Fact]
    public async Task Recommendations_use_insufficient_history_fallback_for_new_user()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createUser = await client.PostAsJsonAsync("/api/users", new { email = "new.user@local.test", password = "NewUserPass1!" });
        createUser.EnsureSuccessStatusCode();

        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "Fallback Client" });
        createdClient.EnsureSuccessStatusCode();
        var clientBody = await createdClient.Content.ReadFromJsonAsync<ClientDto>();
        var createdProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Fallback Project",
            clientId = clientBody!.Id,
            budgetAmount = 900m,
        });
        createdProject.EnsureSuccessStatusCode();
        var project = await createdProject.Content.ReadFromJsonAsync<ProjectDto>();

        var res = await client.PostAsJsonAsync(
            $"/api/assignments/projects/{project!.Id}/recommendations",
            new { requiredSkills = new[] { "c#" } });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<RecommendationResponseDto>();
        Assert.NotNull(body);
        Assert.Contains(body!.Results, r => r.FallbackReason == "insufficient_history");
    }

    [Fact]
    public async Task Recommendations_use_system_fallback_when_scoring_fails()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "Resilience Client" });
        createdClient.EnsureSuccessStatusCode();
        var clientBody = await createdClient.Content.ReadFromJsonAsync<ClientDto>();
        var createdProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Resilience Project",
            clientId = clientBody!.Id,
            budgetAmount = 1800m,
        });
        createdProject.EnsureSuccessStatusCode();
        var project = await createdProject.Content.ReadFromJsonAsync<ProjectDto>();

        var res = await client.PostAsJsonAsync(
            $"/api/assignments/projects/{project!.Id}/recommendations",
            new { requiredSkills = new[] { "__force_error" } });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<RecommendationResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("system_fallback", body!.FallbackMode);
        Assert.NotEmpty(body.Results);
    }

    private static async Task<string> LoginTokenAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);
    private sealed record ClientDto(Guid Id, string Name);
    private sealed record ProjectDto(Guid Id, string Name, Guid ClientId, bool IsActive);
    private sealed record RecommendationResponseDto(string FallbackMode, List<RecommendationResultDto> Results);
    private sealed record RecommendationResultDto(Guid UserId, decimal TotalScore, string? FallbackReason, List<string> Rationale);
}
