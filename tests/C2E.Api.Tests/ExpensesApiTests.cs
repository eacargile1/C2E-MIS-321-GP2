using System.Globalization;
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
    public async Task Create_multipart_with_invoice_then_download()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.invoice@local.test", "IcInvPass1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("2026-03-20"), "expenseDate");
        content.Add(new StringContent("Valent"), "client");
        content.Add(new StringContent("VAL023"), "project");
        content.Add(new StringContent("Travel"), "category");
        content.Add(new StringContent("Taxi receipt"), "description");
        content.Add(new StringContent(12.5m.ToString(CultureInfo.InvariantCulture)), "amount");
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34, 0x0a, 0x25, 0xc4, 0xe3, 0x0c, 0x0a };
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "invoice", "receipt.pdf");

        var create = await client.PostAsync("/api/expenses", content);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExpenseDtoWithInvoice>();
        Assert.NotNull(created);
        Assert.True(created!.HasInvoice);

        var dl = await client.GetAsync($"/api/expenses/{created.Id}/invoice");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal("application/pdf", dl.Content.Headers.ContentType?.MediaType);
        var body = await dl.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, body);
    }

    [Fact]
    public async Task Partner_can_open_expense_approval_queue_team_still_forbidden()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var partnerToken = await CreateUserWithRoleAsync(client, "par.exp@local.test", "ParExpPa1!", "Partner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var pending = await client.GetAsync("/api/expenses/approvals/pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/expenses/team")).StatusCode);
    }

    [Fact]
    public async Task Team_expenses_manager_sees_direct_report_lines_ic_forbidden()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.team@local.test", "IcTeamPa1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.team@local.test", "MgrTeamP1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var icToken = await LoginTokenAsync(client, "ic.team@local.test", "IcTeamPa1!");
        var mgrToken = await LoginTokenAsync(client, "mgr.team@local.test", "MgrTeamP1!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-04-01",
            client = "Valent",
            project = "VAL023",
            category = "Travel",
            description = "Flight",
            amount = 199m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/expenses/team")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var team = await client.GetAsync("/api/expenses/team");
        Assert.Equal(HttpStatusCode.OK, team.StatusCode);
        var teamRows = await team.Content.ReadFromJsonAsync<List<ExpenseDto>>();
        var only = Assert.Single(teamRows!);
        Assert.Equal(199m, only.Amount);
        Assert.Equal("Pending", only.Status);
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

    [Fact]
    public async Task Finance_expense_ledger_sees_all_statuses_IC_forbidden()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.led@local.test", "IcLedPass1!", "IC");
        var finToken = await CreateUserWithRoleAsync(client, "fin.led@local.test", "FinLedPass1!", "Finance");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-03-05",
            client = "Acme",
            project = "ACM-1",
            category = "Travel",
            description = "Cab",
            amount = 22m,
        });
        var pend = await client.PostAsJsonAsync("/api/expenses", new
        {
            expenseDate = "2026-03-06",
            client = "Acme",
            project = "ACM-1",
            category = "Meals",
            description = "Lunch",
            amount = 33m,
        });
        pend.EnsureSuccessStatusCode();
        var createdPend = await pend.Content.ReadFromJsonAsync<ExpenseDto>();

        // IC has no manager assigned; only Admin (or IC's assigned manager) may approve.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        await client.PostAsync($"/api/expenses/{createdPend!.Id}/approve", null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var icLedger = await client.GetAsync("/api/expenses/ledger");
        Assert.Equal(HttpStatusCode.Forbidden, icLedger.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        var finLedger = await client.GetAsync("/api/expenses/ledger");
        Assert.Equal(HttpStatusCode.OK, finLedger.StatusCode);
        var rows = await finLedger.Content.ReadFromJsonAsync<List<ExpenseLedgerDto>>();
        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        Assert.Contains(rows, x => x.Status == "Pending");
        Assert.Contains(rows, x => x.Status == "Approved");
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
    private sealed record ExpenseDtoWithInvoice(Guid Id, decimal Amount, string Status, bool HasInvoice);
    private sealed record ExpenseLedgerDto(string Status);
}
