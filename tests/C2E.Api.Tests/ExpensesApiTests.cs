using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class ExpensesApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"expenses-tests-{Guid.NewGuid():N}");
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

    private static async Task<Guid> CreateUserWithRoleReturnIdAsync(
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
        return created!.Id;
    }

    [Fact]
    public async Task IC_can_create_and_list_own_expenses()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.exp@local.test", "IcExpPass1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        var create = await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-03-02",
            client = "Valent",
            project = "VAL023",
            category = "Meals",
            description = "Mentor lunch",
            amount = 42.33m
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var mine = await client.GetAsync("/api/expenses/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var rows = await mine.Content.ReadFromJsonAsync<List<ExpenseDto>>();
        Assert.NotNull(rows);
        Assert.Single(rows!);
        Assert.Equal("Pending", rows[0].Status);
        Assert.Equal(42.33m, rows[0].Amount);
    }

    [Fact]
    public async Task Manager_can_approve_pending_expense_but_ic_cannot_open_approval_queue()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.appr@local.test", "IcApprPass1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.appr@local.test", "MgrApprPass1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var assignMgr = await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId });
        assignMgr.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        var icToken = await LoginTokenAsync(client, "ic.appr@local.test", "IcApprPass1!");
        var mgrToken = await LoginTokenAsync(client, "mgr.appr@local.test", "MgrApprPass1!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var create = await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-03-03",
            client = "Valent",
            project = "VAL023",
            category = "Travel",
            description = "Parking",
            amount = 18.00m
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ExpenseDto>();

        var icQueue = await client.GetAsync("/api/expenses/approvals/pending");
        Assert.Equal(HttpStatusCode.Forbidden, icQueue.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var mgrQueue = await client.GetAsync("/api/expenses/approvals/pending");
        Assert.Equal(HttpStatusCode.OK, mgrQueue.StatusCode);

        var approve = await client.PostAsync($"/api/expenses/{created!.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var mine = await client.GetAsync("/api/expenses/mine");
        var rows = await mine.Content.ReadFromJsonAsync<List<ExpenseDto>>();
        Assert.Equal("Approved", rows![0].Status);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);
    [Fact]
    public async Task Manager_sees_only_direct_reports_in_pending_queue()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrAId = await CreateUserWithRoleReturnIdAsync(client, "mgr.a@local.test", "MgrAApass1!", "Manager");
        var mgrBId = await CreateUserWithRoleReturnIdAsync(client, "mgr.b@local.test", "MgrBApass1!", "Manager");
        var icAId = await CreateUserWithRoleReturnIdAsync(client, "ic.a@local.test", "IcAApass1!", "IC");
        var icBId = await CreateUserWithRoleReturnIdAsync(client, "ic.b@local.test", "IcBBpass1!", "IC");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icAId}", new { assignManager = true, managerUserId = mgrAId }))
            .IsSuccessStatusCode);
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icBId}", new { assignManager = true, managerUserId = mgrBId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var icAToken = await LoginTokenAsync(client, "ic.a@local.test", "IcAApass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icAToken);
        var create = await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-03-10",
            client = "Valent",
            project = "VAL023",
            category = "Meals",
            description = "Team lunch",
            amount = 55m,
        });
        create.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        var mgrAToken = await LoginTokenAsync(client, "mgr.a@local.test", "MgrAApass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrAToken);
        var queue = await client.GetAsync("/api/expenses/approvals/pending");
        queue.EnsureSuccessStatusCode();
        var rows = await queue.Content.ReadFromJsonAsync<List<ExpenseDto>>();
        Assert.NotNull(rows);
        Assert.Single(rows!);

        var mgrBToken = await LoginTokenAsync(client, "mgr.b@local.test", "MgrBApass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrBToken);
        var queueB = await client.GetAsync("/api/expenses/approvals/pending");
        queueB.EnsureSuccessStatusCode();
        var rowsB = await queueB.Content.ReadFromJsonAsync<List<ExpenseDto>>();
        Assert.NotNull(rowsB);
        Assert.Empty(rowsB!);
    }

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
    private sealed record ExpenseDto(Guid Id, decimal Amount, string Status);
}
