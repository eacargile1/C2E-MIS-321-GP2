using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class ProjectsApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"projects-tests-{Guid.NewGuid():N}");
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
    public async Task Admin_create_list_filter_patch_projects()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var createClient = await client.PostAsJsonAsync("/api/clients", new { name = "Acme" });
        createClient.EnsureSuccessStatusCode();
        var acme = (await createClient.Content.ReadFromJsonAsync<ClientDto>())!;

        var createProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Website Redesign",
            clientId = acme.Id,
            budgetAmount = 50000m,
        });
        Assert.Equal(HttpStatusCode.Created, createProject.StatusCode);
        var project = (await createProject.Content.ReadFromJsonAsync<ProjectDto>())!;
        Assert.Equal("Website Redesign", project.Name);
        Assert.Equal("Acme", project.ClientName);

        var list = await client.GetAsync("/api/projects");
        list.EnsureSuccessStatusCode();
        var rows = (await list.Content.ReadFromJsonAsync<List<ProjectDto>>())!;
        Assert.Single(rows);

        var search = await client.GetAsync("/api/projects?q=redesign");
        search.EnsureSuccessStatusCode();
        Assert.Single((await search.Content.ReadFromJsonAsync<List<ProjectDto>>())!);

        var byClient = await client.GetAsync($"/api/projects?clientId={acme.Id}");
        byClient.EnsureSuccessStatusCode();
        Assert.Single((await byClient.Content.ReadFromJsonAsync<List<ProjectDto>>())!);

        var patch = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", new { isActive = false });
        patch.EnsureSuccessStatusCode();

        var activeOnly = await client.GetAsync("/api/projects");
        activeOnly.EnsureSuccessStatusCode();
        Assert.Empty((await activeOnly.Content.ReadFromJsonAsync<List<ProjectDto>>())!);

        var withInactive = await client.GetAsync("/api/projects?includeInactive=true");
        withInactive.EnsureSuccessStatusCode();
        Assert.Single((await withInactive.Content.ReadFromJsonAsync<List<ProjectDto>>())!);
    }

    [Fact]
    public async Task Only_admin_and_partner_create_projects_manager_finance_forbidden_ic_lists()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var createClient = await client.PostAsJsonAsync("/api/clients", new { name = "Delta" });
        createClient.EnsureSuccessStatusCode();
        var delta = (await createClient.Content.ReadFromJsonAsync<ClientDto>())!;

        var managerToken = await CreateUserWithRoleAsync(client, "mgr.proj@local.test", "MgrProj1!", "Manager");
        var icToken = await CreateUserWithRoleAsync(client, "ic.proj@local.test", "IcProj1!", "IC");
        var partnerToken = await CreateUserWithRoleAsync(client, "par.proj@local.test", "ParProjP1!", "Partner");
        var finToken = await CreateUserWithRoleAsync(client, "fin.proj@local.test", "FinProjP1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var managerCreate = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Manager should not create",
            clientId = delta.Id,
            budgetAmount = 1234m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, managerCreate.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        var finCreate = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Finance should not create",
            clientId = delta.Id,
            budgetAmount = 2m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, finCreate.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icCreate = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Should Fail",
            clientId = delta.Id,
            budgetAmount = 10m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, icCreate.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var partnerCreate = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Partner engagement",
            clientId = delta.Id,
            budgetAmount = 1m,
        });
        Assert.Equal(HttpStatusCode.Created, partnerCreate.StatusCode);

        var partnerProject = (await partnerCreate.Content.ReadFromJsonAsync<ProjectDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icUserId = (await client.GetFromJsonAsync<MeIdDto>("/api/auth/me"))!.Id;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        (await client.PutAsync(
                $"/api/assignments/projects/{partnerProject.Id}/employees/{icUserId}",
                null))
            .EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icListAfter = await client.GetAsync("/api/projects");
        icListAfter.EnsureSuccessStatusCode();
        Assert.Single((await icListAfter.Content.ReadFromJsonAsync<List<ProjectDto>>())!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var managerPatch = await client.PatchAsJsonAsync(
            $"/api/projects/{partnerProject.Id}",
            new { name = "Manager rename attempt" });
        Assert.Equal(HttpStatusCode.Forbidden, managerPatch.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var partnerPatch = await client.PatchAsJsonAsync(
            $"/api/projects/{partnerProject.Id}",
            new { name = "Partner rename ok" });
        partnerPatch.EnsureSuccessStatusCode();
        var patched = await partnerPatch.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal("Partner rename ok", patched!.Name);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);
    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
    private sealed record MeIdDto(Guid Id, string Email, string DisplayName, string Role, bool IsActive);
    private sealed record ClientDto(Guid Id, string Name);
    private sealed record ProjectDto(
        Guid Id,
        string Name,
        Guid ClientId,
        string ClientName,
        decimal BudgetAmount,
        bool IsActive);
}
