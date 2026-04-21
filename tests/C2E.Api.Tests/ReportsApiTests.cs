using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class ReportsApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"reports-tests-{Guid.NewGuid():N}");
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

    private static async Task<(Guid Id, string Token)> CreateUserWithRoleAndTokenAsync(
        HttpClient client,
        string email,
        string password,
        string role)
    {
        var token = await CreateUserWithRoleAsync(client, email, password, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        var dto = await me.Content.ReadFromJsonAsync<MeDto>();
        client.DefaultRequestHeaders.Authorization = null;
        return (dto!.Id, token);
    }

    [Fact]
    public async Task Personal_summary_missing_from_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.sum.missingfrom@local.test", "MgrSumMissFrom1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-summary?to=2026-04-30");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_summary_missing_to_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.sum.missingto@local.test", "MgrSumMissTo1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-summary?from=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_detail_happy_path_groups_by_client_project_ordered_by_total_hours()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.rep@local.test", "MgrRepPass1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);

        const string weekStart = "2026-04-06";
        var put = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 5m,
                    isBillable = true,
                    notes = (string?)null,
                },
                new
                {
                    workDate = "2026-04-08",
                    client = "Beta",
                    project = "Mobile",
                    task = "QA",
                    hours = 3m,
                    isBillable = false,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var res = await client.GetAsync("/api/reports/personal-detail?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PersonalDetailResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Rows.Count);
        Assert.Equal("Acme", body.Rows[0].Client);
        Assert.Equal("Website", body.Rows[0].Project);
        Assert.Equal(5m, body.Rows[0].TotalHours);
        Assert.Equal(5m, body.Rows[0].BillableHours);
        Assert.Equal(0m, body.Rows[0].NonBillableHours);
        Assert.Equal("Beta", body.Rows[1].Client);
        Assert.Equal(3m, body.Rows[1].TotalHours);
        Assert.Equal(0m, body.Rows[1].BillableHours);
        Assert.Equal(3m, body.Rows[1].NonBillableHours);

        var sumTotal = body.Rows.Sum(r => r.TotalHours);
        var sumBill = body.Rows.Sum(r => r.BillableHours);
        var sumNon = body.Rows.Sum(r => r.NonBillableHours);
        Assert.Equal(8m, sumTotal);
        Assert.Equal(5m, sumBill);
        Assert.Equal(3m, sumNon);
    }

    [Fact]
    public async Task Personal_detail_IC_returns_403()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.rep@local.test", "IcRepPass1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var res = await client.GetAsync("/api/reports/personal-detail?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Personal_detail_missing_from_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.rep2@local.test", "MgrRep2Pass1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-detail?to=2026-04-30");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_detail_missing_to_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.rep2b@local.test", "MgrRep2bPass1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-detail?from=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_detail_invalid_date_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.rep3@local.test", "MgrRep3Pass1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-detail?from=not-a-date&to=2026-04-30");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_detail_to_before_from_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.rep4@local.test", "MgrRep4Pass1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/personal-detail?from=2026-04-10&to=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_manager_sees_only_direct_reports()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var (managerId, managerToken) = await CreateUserWithRoleAndTokenAsync(client, "mgr.team@local.test", "MgrTeamPass1!", "Manager");
        var (directId, directToken) = await CreateUserWithRoleAndTokenAsync(client, "ic.direct@local.test", "IcDirect1!", "IC");
        var (_, otherToken) = await CreateUserWithRoleAndTokenAsync(client, "ic.other@local.test", "IcOther1!", "IC");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var assign = await client.PatchAsJsonAsync($"/api/users/{directId}", new { assignManager = true, managerUserId = managerId });
        assign.EnsureSuccessStatusCode();

        const string weekStart = "2026-04-06";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", directToken);
        var directPut = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "DirectProj",
                    task = "Build",
                    hours = 6m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, directPut.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var otherPut = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Beta",
                    project = "OtherProj",
                    task = "Build",
                    hours = 8m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, otherPut.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<TeamSummaryResponseDto>();
        Assert.NotNull(body);
        Assert.Single(body!.Rows);
        Assert.Equal(directId, body.Rows[0].UserId);
        Assert.Equal(6m, body.Rows[0].TotalHours);
    }

    [Fact]
    public async Task Team_summary_admin_sees_all_active_users()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var adminToken = await AdminTokenAsync(client);
        var (_, managerToken) = await CreateUserWithRoleAndTokenAsync(client, "mgr.all@local.test", "MgrAllPass1!", "Manager");
        var (icAId, icAToken) = await CreateUserWithRoleAndTokenAsync(client, "ic.a.all@local.test", "IcAAllPass1!", "IC");
        var (icBId, icBToken) = await CreateUserWithRoleAndTokenAsync(client, "ic.b.all@local.test", "IcBAllPass1!", "IC");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var managerMe = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.NotNull(managerMe);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await client.PatchAsJsonAsync($"/api/users/{icAId}", new { assignManager = true, managerUserId = managerMe!.Id }))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/users/{icBId}", new { assignManager = true, managerUserId = managerMe.Id }))
            .EnsureSuccessStatusCode();

        const string weekStart = "2026-04-06";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icAToken);
        (await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "ProjA",
                    task = "Build",
                    hours = 4m,
                    isBillable = true,
                    notes = (string?)null,
                },
            })).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icBToken);
        (await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "ProjB",
                    task = "Build",
                    hours = 2m,
                    isBillable = false,
                    notes = (string?)null,
                },
            })).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<TeamSummaryResponseDto>();
        Assert.NotNull(body);
        Assert.Contains(body!.Rows, r => r.UserId == icAId);
        Assert.Contains(body.Rows, r => r.UserId == icBId);
    }

    [Fact]
    public async Task Team_summary_ic_gets_403()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icToken = await CreateUserWithRoleAsync(client, "ic.team.forbid@local.test", "IcTeamForb1!", "IC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_finance_gets_403()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var finToken = await CreateUserWithRoleAsync(client, "fin.team.forbid@local.test", "FinTeamForb1!", "Finance");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_partner_gets_403()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var partnerToken = await CreateUserWithRoleAsync(client, "par.team.forbid@local.test", "ParTeamForb1!", "Partner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_missing_from_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.team.missingfrom@local.test", "MgrMissFrom1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/team-summary?to=2026-04-30");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_missing_to_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.team.missingto@local.test", "MgrMissTo1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_invalid_date_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.team.bad@local.test", "MgrTeamBad1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=bad-date&to=2026-04-30");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_to_before_from_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.team.range@local.test", "MgrTeamRng1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-10&to=2026-04-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Team_summary_manager_with_no_direct_reports_returns_empty_rows()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.empty@local.test", "MgrEmptyP1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var res = await client.GetAsync("/api/reports/team-summary?from=2026-04-01&to=2026-04-30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<TeamSummaryResponseDto>();
        Assert.NotNull(body);
        Assert.Empty(body!.Rows);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record MeDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record PersonalDetailRowDto(
        string Client,
        string Project,
        decimal TotalHours,
        decimal BillableHours,
        decimal NonBillableHours);

    private sealed record PersonalDetailResponseDto(
        string From,
        string To,
        List<PersonalDetailRowDto> Rows);

    private sealed record TeamMemberSummaryRowDto(
        Guid UserId,
        string Email,
        string DisplayName,
        string Role,
        decimal TotalHours,
        decimal BillableHours,
        decimal NonBillableHours,
        int TimesheetLineCount,
        int ExpenseCount,
        decimal ExpensePendingTotal,
        decimal ExpenseApprovedTotal);

    private sealed record TeamSummaryResponseDto(
        string From,
        string To,
        List<TeamMemberSummaryRowDto> Rows);
}
