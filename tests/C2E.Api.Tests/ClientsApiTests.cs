using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class ClientsApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"clients-tests-{Guid.NewGuid():N}");
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
        var mgrId = await ApiTestUsers.SeededDevManagerIdAsync(client);
        var create = await client.PostAsJsonAsync("/api/users", new { email, password, managerUserId = mgrId });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        await ApiTestUsers.PatchUserRoleFromBootstrapIcAsync(client, created!.Id, role);
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

    [Fact]
    public async Task Admin_create_list_patch_and_search()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var create = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                name = "Acme Corp",
                contactEmail = "ops@acme.test",
                defaultBillingRate = 175.50m,
                notes = "Key account",
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createdBody = await create.Content.ReadFromJsonAsync<ClientDto>();
        Assert.NotNull(createdBody);
        Assert.Equal("Acme Corp", createdBody!.Name);
        Assert.Equal(175.50m, createdBody.DefaultBillingRate);

        var list = await client.GetAsync("/api/clients");
        list.EnsureSuccessStatusCode();
        var items = await list.Content.ReadFromJsonAsync<List<ClientDto>>();
        Assert.Single(items!);
        Assert.Equal("Acme Corp", items![0].Name);

        var search = await client.GetAsync("/api/clients?q=acme");
        search.EnsureSuccessStatusCode();
        var found = await search.Content.ReadFromJsonAsync<List<ClientDto>>();
        Assert.Single(found!);

        var none = await client.GetAsync("/api/clients?q=zzz");
        none.EnsureSuccessStatusCode();
        Assert.Empty((await none.Content.ReadFromJsonAsync<List<ClientDto>>())!);

        var patch = await client.PatchAsJsonAsync(
            $"/api/clients/{createdBody.Id}",
            new { name = "Acme Ltd", isActive = false });
        patch.EnsureSuccessStatusCode();
        var patched = await patch.Content.ReadFromJsonAsync<ClientDto>();
        Assert.Equal("Acme Ltd", patched!.Name);
        Assert.False(patched.IsActive);

        var activeOnly = await client.GetAsync("/api/clients");
        activeOnly.EnsureSuccessStatusCode();
        Assert.Empty((await activeOnly.Content.ReadFromJsonAsync<List<ClientDto>>())!);

        var withInactive = await client.GetAsync("/api/clients?includeInactive=true");
        withInactive.EnsureSuccessStatusCode();
        Assert.Single((await withInactive.Content.ReadFromJsonAsync<List<ClientDto>>())!);
    }

    [Fact]
    public async Task IC_list_hides_billing_rate_Finance_sees_it()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var betaCreate = await client.PostAsJsonAsync(
            "/api/clients",
            new { name = "Beta LLC", defaultBillingRate = 200m });
        betaCreate.EnsureSuccessStatusCode();
        var betaId = (await betaCreate.Content.ReadFromJsonAsync<ClientDto>())!.Id;

        var icToken = await CreateUserWithRoleAsync(client, "ic.cl@local.test", "IcClPass1!", "IC");
        var finToken = await CreateUserWithRoleAsync(client, "fin.cl2@local.test", "FinCl2Pass1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icUserId = (await client.GetFromJsonAsync<MeIdDto>("/api/auth/me"))!.Id;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        (await client.PutAsync($"/api/assignments/clients/{betaId}/employees/{icUserId}", null)).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icList = await client.GetAsync("/api/clients");
        icList.EnsureSuccessStatusCode();
        var icRow = (await icList.Content.ReadFromJsonAsync<List<ClientDto>>())![0];
        Assert.Null(icRow.DefaultBillingRate);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        var finList = await client.GetAsync("/api/clients");
        finList.EnsureSuccessStatusCode();
        var finRow = (await finList.Content.ReadFromJsonAsync<List<ClientDto>>())![0];
        Assert.Equal(200m, finRow.DefaultBillingRate);
    }

    [Fact]
    public async Task IC_cannot_patch_create()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.only@local.test", "IcOnlyPass1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icUserId = (await client.GetFromJsonAsync<MeIdDto>("/api/auth/me"))!.Id;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync("/api/clients", new { name = "Gamma" });
        var id = (await create.Content.ReadFromJsonAsync<ClientDto>())!.Id;
        (await client.PutAsync($"/api/assignments/clients/{id}/employees/{icUserId}", null)).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/clients", new { name = "Hack" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/clients/{id}", new { name = "Hack" })).StatusCode);

        var getOk = await client.GetAsync($"/api/clients/{id}");
        getOk.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Partner_can_create_client()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        await CreateUserWithRoleAsync(client, "fin.parcl2@local.test", "FinParCl2!", "Finance");
        var partnerToken = await CreateUserWithRoleAsync(client, "par.cl2@local.test", "ParCl2Pa1!", "Partner");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var userRows = await client.GetFromJsonAsync<List<ApiTestUsers.UserListRow>>("/api/users");
        var finId = userRows!.First(u =>
            string.Equals(u.Email, "fin.parcl2@local.test", StringComparison.OrdinalIgnoreCase)).Id;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var res = await client.PostAsJsonAsync(
            "/api/clients",
            new { name = "FromPartnerCo", financeLeadUserId = finId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task Partner_create_without_finance_lead_is_bad_request()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var partnerToken = await CreateUserWithRoleAsync(client, "par.nofin@local.test", "ParNoFin1!", "Partner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var res = await client.PostAsJsonAsync("/api/clients", new { name = "NoFinCo" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Non_admin_get_inactive_client_returns_404()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync("/api/clients", new { name = "Delta" });
        var id = (await create.Content.ReadFromJsonAsync<ClientDto>())!.Id;
        await client.PatchAsJsonAsync($"/api/clients/{id}", new { isActive = false });

        var icToken = await CreateUserWithRoleAsync(client, "ic.delta@local.test", "IcDelta1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/clients/{id}")).StatusCode);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record MeIdDto(Guid Id, string Email, string DisplayName, string Role, bool IsActive);

    private sealed record ClientDto(Guid Id, string Name, decimal? DefaultBillingRate, bool IsActive);
}
