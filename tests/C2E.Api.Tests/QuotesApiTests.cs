using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class QuotesApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"quotes-tests-{Guid.NewGuid():N}");
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

    private static async Task<string> CreateUserWithRoleAsync(
        HttpClient client,
        string email,
        string password,
        string role)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync("/api/users", new { email, password });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        var patch = await client.PatchAsJsonAsync($"/api/users/{created!.Id}", new { role });
        patch.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

    [Fact]
    public async Task Finance_lists_and_creates_quotes()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await AdminTokenAsync(client);
        var finToken = await CreateUserWithRoleAsync(client, "fin.q@local.test", "FinQPass1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "QuoteCo", defaultBillingRate = 150m });
        createdClient.EnsureSuccessStatusCode();
        var cl = await createdClient.Content.ReadFromJsonAsync<ClientIdDto>();
        Assert.NotNull(cl);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        var empty = await client.GetAsync("/api/quotes");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var list0 = await empty.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.NotNull(list0);
        Assert.Empty(list0!);

        var post = await client.PostAsJsonAsync("/api/quotes", new
        {
            clientId = cl!.Id,
            title = "SOW slice",
            scopeSummary = "MVP",
            estimatedHours = 80m,
            hourlyRate = 150m,
            status = "Sent",
        });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var q = await post.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(q);
        Assert.Equal("Sent", q!.Status);
        Assert.Equal(12000m, q.TotalAmount);

        var list1 = await client.GetAsync("/api/quotes");
        var rows = await list1.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.Single(rows!);
    }

    [Fact]
    public async Task IC_cannot_access_quotes()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.q@local.test", "IcQPass1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/quotes")).StatusCode);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);
    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
    private sealed record ClientIdDto(Guid Id);
    private sealed record QuoteDto(Guid Id, decimal TotalAmount, string Status);
}
