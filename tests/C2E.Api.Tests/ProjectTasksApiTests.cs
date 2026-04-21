using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class ProjectTasksApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"project-tasks-tests-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "admin@local.test");
            b.UseSetting("Seed:DevUserPassword", "AdminPass!9");
        });

    [Fact]
    public async Task Manager_can_create_list_patch_delete_project_task()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var seedMgrId = await ApiTestUsers.SeededDevManagerIdAsync(client);
        var seedParId = await ApiTestUsers.SeededDevPartnerIdAsync(client);
        var mgrCreate = await client.PostAsJsonAsync(
            "/api/users",
            new
            {
                email = "mgr.pt@local.test",
                password = "MgrPtPass1!",
                role = "Manager",
                managerUserId = seedMgrId,
                partnerUserId = seedParId,
            });
        mgrCreate.EnsureSuccessStatusCode();

        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "PT Client" });
        createdClient.EnsureSuccessStatusCode();
        var clientBody = await createdClient.Content.ReadFromJsonAsync<ClientDto>();

        var createdProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "PT Project",
            clientId = clientBody!.Id,
            budgetAmount = 2000m,
        });
        createdProject.EnsureSuccessStatusCode();
        var project = await createdProject.Content.ReadFromJsonAsync<ProjectDto>();

        client.DefaultRequestHeaders.Authorization = null;
        var mgrToken = await LoginTokenAsync(client, "mgr.pt@local.test", "MgrPtPass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);

        var createTask = await client.PostAsJsonAsync(
            "/api/project-tasks",
            new
            {
                projectId = project!.Id,
                title = "Data migration",
                description = "Cutover weekend",
                requiredSkills = new[] { "sql", "c#" },
                dueDate = "2026-05-01",
            });
        Assert.Equal(HttpStatusCode.Created, createTask.StatusCode);
        var task = await createTask.Content.ReadFromJsonAsync<ProjectTaskResponseDto>();
        Assert.NotNull(task);
        Assert.Equal("Open", task!.Status);
        Assert.Equal(2, task.RequiredSkills.Count);

        var list = await client.GetAsync("/api/project-tasks");
        list.EnsureSuccessStatusCode();
        var tasks = await list.Content.ReadFromJsonAsync<List<ProjectTaskResponseDto>>();
        Assert.NotNull(tasks);
        Assert.Contains(tasks!, t => t.Id == task.Id);

        var patch = await client.PatchAsJsonAsync(
            $"/api/project-tasks/{task.Id}",
            new { status = "InProgress", title = "Data migration (phase 1)" });
        patch.EnsureSuccessStatusCode();
        var patched = await patch.Content.ReadFromJsonAsync<ProjectTaskResponseDto>();
        Assert.Equal("InProgress", patched!.Status);
        Assert.Equal("Data migration (phase 1)", patched.Title);

        var del = await client.DeleteAsync($"/api/project-tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Task_recommendations_endpoint_returns_ranked_results()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createdClient = await client.PostAsJsonAsync("/api/clients", new { name = "Reco PT Client" });
        createdClient.EnsureSuccessStatusCode();
        var clientBody = await createdClient.Content.ReadFromJsonAsync<ClientDto>();

        var createdProject = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Reco PT Project",
            clientId = clientBody!.Id,
            budgetAmount = 1500m,
        });
        createdProject.EnsureSuccessStatusCode();
        var project = await createdProject.Content.ReadFromJsonAsync<ProjectDto>();

        var createTask = await client.PostAsJsonAsync(
            "/api/project-tasks",
            new
            {
                projectId = project!.Id,
                title = "Need analyst",
                requiredSkills = new[] { "admin", "manager" },
            });
        createTask.EnsureSuccessStatusCode();
        var task = await createTask.Content.ReadFromJsonAsync<ProjectTaskResponseDto>();

        var res = await client.PostAsync($"/api/project-tasks/{task!.Id}/recommendations", null);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<StaffingRecommendationResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Results);
        Assert.All(body.Results, r => Assert.InRange(r.TotalScore, 0m, 1m));
    }

    [Fact]
    public async Task IC_cannot_access_project_tasks_list()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await LoginTokenAsync(client, "admin@local.test", "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var seedMgrForIc = await ApiTestUsers.SeededDevManagerIdAsync(client);
        var icCreate = await client.PostAsJsonAsync(
            "/api/users",
            new
            {
                email = "ic.pt@local.test",
                password = "IcPtPass1!",
                role = "IC",
                managerUserId = seedMgrForIc,
            });
        icCreate.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;
        var icToken = await LoginTokenAsync(client, "ic.pt@local.test", "IcPtPass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        var list = await client.GetAsync("/api/project-tasks");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
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

    private sealed record ProjectTaskResponseDto(
        Guid Id,
        Guid ProjectId,
        string ClientName,
        string ProjectName,
        string Title,
        string? Description,
        List<string> RequiredSkills,
        string? DueDate,
        Guid? AssignedUserId,
        string? AssignedEmail,
        string Status,
        Guid CreatedByUserId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record StaffingRecommendationResponseDto(
        string FallbackMode,
        List<StaffingRecommendationResultDto> Results,
        string? WarningMessage);

    private sealed record StaffingRecommendationResultDto(
        Guid UserId,
        string Email,
        string DisplayName,
        string Role,
        decimal TotalScore,
        decimal SkillScore,
        decimal AvailabilityScore,
        decimal UtilizationScore,
        List<string> Rationale,
        string? FallbackReason);
}
