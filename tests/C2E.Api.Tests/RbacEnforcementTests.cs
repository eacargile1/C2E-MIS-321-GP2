using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class RbacEnforcementTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"rbac-tests-{Guid.NewGuid():N}");
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

    /// <summary>Admin creates IC user, returns login token for that user.</summary>
    private static async Task<string> CreateUserAndGetTokenAsync(
        HttpClient client,
        string email,
        string password)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email, password });
        create.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

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
        var patch = await client.PatchAsJsonAsync(
            $"/api/users/{created!.Id}",
            new { role });
        patch.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

    private static async Task AssertForbiddenJsonAsync(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal(AuthMessages.Forbidden, err?.Message);
    }

    [Fact]
    public async Task Rbac_stub_routes_return_401_when_anonymous()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/timesheets/organization")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/clients/billing-rates")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/clients", new { name = "x" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/invoices/generate", null)).StatusCode);
    }

    [Fact]
    public async Task Organization_timesheets_IC_forbidden_Finance_ok()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserAndGetTokenAsync(client, "ic.ts@local.test", "IcTsPass1!");
        var financeToken = await CreateUserWithRoleAsync(
            client, "fin.ts@local.test", "FinTsPass1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await AssertForbiddenJsonAsync(await client.GetAsync("/api/timesheets/organization"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", financeToken);
        var ok = await client.GetAsync("/api/timesheets/organization");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Billing_rates_IC_and_Manager_forbidden_Finance_ok()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserAndGetTokenAsync(client, "ic.br@local.test", "IcBrPass1!");
        var mgrToken = await CreateUserWithRoleAsync(
            client, "mgr.br@local.test", "MgrBrPass1!", "Manager");
        var financeToken = await CreateUserWithRoleAsync(
            client, "fin.br@local.test", "FinBrPass1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await AssertForbiddenJsonAsync(await client.GetAsync("/api/clients/billing-rates"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        await AssertForbiddenJsonAsync(await client.GetAsync("/api/clients/billing-rates"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", financeToken);
        var ok = await client.GetAsync("/api/clients/billing-rates");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Create_client_Finance_forbidden_Admin_ok()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var financeToken = await CreateUserWithRoleAsync(
            client, "fin.cl@local.test", "FinClPass1!", "Finance");
        var adminToken = await AdminTokenAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", financeToken);
        await AssertForbiddenJsonAsync(await client.PostAsJsonAsync("/api/clients", new { name = "x" }));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var created = await client.PostAsJsonAsync("/api/clients", new { name = "x" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Invoice_generate_Manager_forbidden_Finance_and_Admin_ok()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(
            client, "mgr.inv@local.test", "MgrInvPass1!", "Manager");
        var financeToken = await CreateUserWithRoleAsync(
            client, "fin.inv@local.test", "FinInvPass1!", "Finance");
        var adminToken = await AdminTokenAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        await AssertForbiddenJsonAsync(await client.PostAsync("/api/invoices/generate", null));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", financeToken);
        var finRes = await client.PostAsync("/api/invoices/generate", null);
        Assert.Equal(HttpStatusCode.NoContent, finRes.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminRes = await client.PostAsync("/api/invoices/generate", null);
        Assert.Equal(HttpStatusCode.NoContent, adminRes.StatusCode);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record AuthErrorDto(string Message);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
}
