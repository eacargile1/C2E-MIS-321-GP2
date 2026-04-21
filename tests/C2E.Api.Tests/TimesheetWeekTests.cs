using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class TimesheetWeekTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"timesheet-week-tests-{Guid.NewGuid():N}");
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

    private static async Task<string> CreateIcUserAndGetTokenAsync(
        HttpClient client,
        string email,
        string password)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync("/api/users", new { email, password });
        create.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

    private static async Task<Guid> MyUserIdAsync(HttpClient client)
    {
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        var data = await me.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(data);
        Assert.True(Guid.TryParse(data!.Id, out var id));
        return id;
    }

    [Fact]
    public async Task Put_week_then_get_week_round_trips_for_same_user()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var token = await CreateIcUserAndGetTokenAsync(client, "ic1@local.test", "IcOnePass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string weekStart = "2026-04-06"; // Monday

        var put = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 7.5m,
                    isBillable = true,
                    notes = (string?)"Did work",
                },
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "Website",
                    task = "Meetings",
                    hours = 1.25m,
                    isBillable = false,
                    notes = (string?)null,
                },
            });

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await client.GetAsync($"/api/timesheets/week?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var lines = await get.Content.ReadFromJsonAsync<List<TimesheetLineDto>>();
        Assert.NotNull(lines);
        Assert.Equal(2, lines!.Count);
        Assert.Contains(lines, l => l.WorkDate == "2026-04-06" && l.Client == "Acme" && l.Hours == 7.5m && l.IsBillable);
        Assert.Contains(lines, l => l.WorkDate == "2026-04-07" && l.Task == "Meetings" && l.Hours == 1.25m && !l.IsBillable);
    }

    [Fact]
    public async Task Different_user_cannot_read_or_overwrite_another_users_week()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var token1 = await CreateIcUserAndGetTokenAsync(client, "ic1@local.test", "IcOnePass1!");
        var token2 = await CreateIcUserAndGetTokenAsync(client, "ic2@local.test", "IcTwoPass1!");

        const string weekStart = "2026-04-06"; // Monday

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var user1Id = await MyUserIdAsync(client);
        var put1 = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 2m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, put1.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var getOther = await client.GetAsync($"/api/timesheets/users/{user1Id}/week?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.Forbidden, getOther.StatusCode);
        var putOther = await client.PutAsJsonAsync(
            $"/api/timesheets/users/{user1Id}/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 9m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.Forbidden, putOther.StatusCode);

        var get2 = await client.GetAsync($"/api/timesheets/week?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.OK, get2.StatusCode);
        var lines2 = await get2.Content.ReadFromJsonAsync<List<TimesheetLineDto>>();
        Assert.NotNull(lines2);
        Assert.Empty(lines2!);

        var put2 = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 9m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, put2.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var get1 = await client.GetAsync($"/api/timesheets/week?weekStart={weekStart}");
        var lines1 = await get1.Content.ReadFromJsonAsync<List<TimesheetLineDto>>();
        Assert.NotNull(lines1);
        Assert.Single(lines1!);
        Assert.Equal(2m, lines1![0].Hours);
    }

    [Fact]
    public async Task IC_cannot_save_week_while_pending_approval()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var token = await CreateIcUserAndGetTokenAsync(client, "ic.pending@local.test", "IcPendPa1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var icId = await MyUserIdAsync(client);
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.icpend@local.test", "MgrIcPend1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string weekStart = "2026-04-06";
        var putOk = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 3m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, putOk.StatusCode);

        var submit = await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);

        var putBlocked = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 4m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.BadRequest, putBlocked.StatusCode);
    }

    [Fact]
    public async Task Manager_pending_weeks_only_direct_reports()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.ts.mgr@local.test", "IcTsMgrP1!", "IC");
        var mgrAId = await CreateUserWithRoleReturnIdAsync(client, "mgr.ts.a@local.test", "MgrTsA1!", "Manager");
        var mgrBId = await CreateUserWithRoleReturnIdAsync(client, "mgr.ts.b@local.test", "MgrTsB1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrAId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        const string weekStart = "2026-04-06";
        var icToken = await LoginTokenAsync(client, "ic.ts.mgr@local.test", "IcTsMgrP1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 2m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        var submit = await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var mgrBToken = await LoginTokenAsync(client, "mgr.ts.b@local.test", "MgrTsB1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrBToken);
        var bList = await client.GetAsync("/api/timesheets/approvals/pending-weeks");
        Assert.Equal(HttpStatusCode.OK, bList.StatusCode);
        var bRows = await bList.Content.ReadFromJsonAsync<List<PendingTimesheetWeekDto>>();
        Assert.NotNull(bRows);
        Assert.Empty(bRows!);

        var mgrAToken = await LoginTokenAsync(client, "mgr.ts.a@local.test", "MgrTsA1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrAToken);
        var aList = await client.GetAsync("/api/timesheets/approvals/pending-weeks");
        Assert.Equal(HttpStatusCode.OK, aList.StatusCode);
        var aRows = await aList.Content.ReadFromJsonAsync<List<PendingTimesheetWeekDto>>();
        Assert.NotNull(aRows);
        Assert.Single(aRows!);
        Assert.Equal(icId, aRows[0].UserId);
        Assert.Equal(weekStart, aRows[0].WeekStart);
    }

    [Fact]
    public async Task Manager_approve_then_IC_edit_clears_approval_row()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.ts.apr@local.test", "IcTsApr1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.ts.apr@local.test", "MgrTsApr1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        const string weekStart = "2026-04-06";
        var icToken = await LoginTokenAsync(client, "ic.ts.apr@local.test", "IcTsApr1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 5m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null)).StatusCode);

        var mgrToken = await LoginTokenAsync(client, "mgr.ts.apr@local.test", "MgrTsApr1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var approve = await client.PostAsync($"/api/timesheets/approvals/week/{icId}/approve?weekStart={weekStart}", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var stApproved = await client.GetAsync($"/api/timesheets/week/status?weekStart={weekStart}");
        stApproved.EnsureSuccessStatusCode();
        var st1 = await stApproved.Content.ReadFromJsonAsync<TimesheetWeekStatusDto>();
        Assert.NotNull(st1);
        Assert.Equal("Approved", st1!.Status);

        var putAfter = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Build",
                    hours = 6m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, putAfter.StatusCode);

        var stCleared = await client.GetAsync($"/api/timesheets/week/status?weekStart={weekStart}");
        stCleared.EnsureSuccessStatusCode();
        var st2 = await stCleared.Content.ReadFromJsonAsync<TimesheetWeekStatusDto>();
        Assert.NotNull(st2);
        Assert.Equal("None", st2!.Status);
    }

    [Fact]
    public async Task Manager_submit_week_requires_billable_engagement_partner_projects()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var mgrToken = await CreateUserWithRoleAsync(client, "mgr.nosub@local.test", "MgrNoSub1!", "Manager");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var r = await client.PostAsync("/api/timesheets/week/submit?weekStart=2026-04-06", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Finance_can_submit_week_when_org_manager_assigned()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var finId = await CreateUserWithRoleReturnIdAsync(client, "fin.tsweek@local.test", "FinTsWeek1!", "Finance");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.fintsw@local.test", "MgrFinTs1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{finId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var finToken = await LoginTokenAsync(client, "fin.tsweek@local.test", "FinTsWeek1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);
        const string weekStart = "2026-04-06";
        var put = await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "Finance work",
                    hours = 1m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var submit = await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    [Fact]
    public async Task Partner_submit_week_requires_billable_engagement_partner_projects()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var parToken = await CreateUserWithRoleAsync(client, "par.nosub@local.test", "ParNoSub1!", "Partner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parToken);
        var r = await client.PostAsync("/api/timesheets/week/submit?weekStart=2026-04-06", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Manager_pending_review_returns_full_lines_for_direct_report()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.review@local.test", "IcReview1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.review@local.test", "MgrReview1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        const string weekStart = "2026-04-06";
        var icToken = await LoginTokenAsync(client, "ic.review@local.test", "IcReview1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-07",
                    client = "Acme",
                    project = "Website",
                    task = "Design review",
                    hours = 2.5m,
                    isBillable = true,
                    notes = (string?)"Client asked for extra mockups",
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var mgrToken = await LoginTokenAsync(client, "mgr.review@local.test", "MgrReview1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var review = await client.GetAsync($"/api/timesheets/approvals/week/{icId}/pending-review?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var body = await review.Content.ReadFromJsonAsync<PendingWeekReviewDto>();
        Assert.NotNull(body);
        Assert.Equal("ic.review@local.test", body!.UserEmail);
        Assert.Single(body.Lines);
        Assert.Equal("Acme", body.Lines[0].Client);
        Assert.Equal("Website", body.Lines[0].Project);
        Assert.Equal("Design review", body.Lines[0].Task);
        Assert.Equal(2.5m, body.Lines[0].Hours);
        Assert.True(body.Lines[0].IsBillable);
        Assert.Equal("Client asked for extra mockups", body.Lines[0].Notes);
    }

    [Fact]
    public async Task Manager_pending_review_forbidden_for_non_direct_report()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.nomgr@local.test", "IcNoMgr1!", "IC");
        var mgrAId = await CreateUserWithRoleReturnIdAsync(client, "mgr.a.rev@local.test", "MgrARev1!", "Manager");
        var mgrBId = await CreateUserWithRoleReturnIdAsync(client, "mgr.b.rev@local.test", "MgrBRev1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrAId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        const string weekStart = "2026-04-06";
        var icToken = await LoginTokenAsync(client, "ic.nomgr@local.test", "IcNoMgr1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={weekStart}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "Acme",
                    project = "Website",
                    task = "T",
                    hours = 1m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        await client.PostAsync($"/api/timesheets/week/submit?weekStart={weekStart}", null);
        client.DefaultRequestHeaders.Authorization = null;

        var mgrBToken = await LoginTokenAsync(client, "mgr.b.rev@local.test", "MgrBRev1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrBToken);
        var review = await client.GetAsync($"/api/timesheets/approvals/week/{icId}/pending-review?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
    }

    [Fact]
    public async Task Manager_pending_review_not_found_when_not_pending()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.nopend@local.test", "IcNoPen1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.nopend@local.test", "MgrNoPen1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        const string weekStart = "2026-04-06";
        var mgrToken = await LoginTokenAsync(client, "mgr.nopend@local.test", "MgrNoPen1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var review = await client.GetAsync($"/api/timesheets/approvals/week/{icId}/pending-review?weekStart={weekStart}");
        Assert.Equal(HttpStatusCode.NotFound, review.StatusCode);
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
        return created.Id;
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
        var patch = await client.PatchAsJsonAsync($"/api/users/{created!.Id}", new { role });
        patch.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        return await LoginTokenAsync(client, email, password);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record MeDto(string Id, string Email, string Role, bool IsActive);

    private sealed record TimesheetLineDto(
        string WorkDate,
        string Client,
        string Project,
        string Task,
        decimal Hours,
        bool IsBillable,
        string? Notes);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record PendingTimesheetWeekDto(Guid UserId, string UserEmail, string WeekStart, decimal TotalHours, decimal BillableHours);

    private sealed record TimesheetWeekStatusDto(string WeekStart, string Status, decimal TotalHours, decimal BillableHours);

    private sealed record PendingWeekReviewLineDto(
        string WorkDate,
        string Client,
        string Project,
        string Task,
        decimal Hours,
        bool IsBillable,
        string? Notes);

    private sealed record ProjectBudgetBarTestDto(
        string ClientName,
        string ProjectName,
        decimal BudgetAmount,
        decimal? DefaultHourlyRate,
        decimal ConsumedBillableAmount,
        decimal PendingSubmissionBillableAmount,
        decimal PendingBillableHours,
        bool CatalogMatched);

    private sealed record PendingWeekReviewDto(
        Guid UserId,
        string UserEmail,
        string WeekStart,
        DateTime SubmittedAtUtc,
        List<PendingWeekReviewLineDto> Lines,
        List<ProjectBudgetBarTestDto>? ProjectBudgetBars = null);

    [Fact]
    public async Task Manager_pending_review_includes_project_budget_bars()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var icId = await CreateUserWithRoleReturnIdAsync(client, "ic.budget@local.test", "IcBudget1!", "IC");
        var mgrId = await CreateUserWithRoleReturnIdAsync(client, "mgr.budget@local.test", "MgrBudget1!", "Manager");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        Assert.True((await client.PatchAsJsonAsync($"/api/users/{icId}", new { assignManager = true, managerUserId = mgrId }))
            .IsSuccessStatusCode);

        var clientRes = await client.PostAsJsonAsync(
            "/api/clients",
            new { name = "BudgetCo", defaultBillingRate = 100m });
        clientRes.EnsureSuccessStatusCode();
        var createdClient = await clientRes.Content.ReadFromJsonAsync<CreatedEntityIdDto>();
        var projRes = await client.PostAsJsonAsync(
            "/api/projects",
            new { name = "BudgetProj", clientId = createdClient!.Id, budgetAmount = 10_000m });
        projRes.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        const string week1 = "2026-04-06";
        const string week2 = "2026-04-13";
        var icToken = await LoginTokenAsync(client, "ic.budget@local.test", "IcBudget1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={week1}",
            new[]
            {
                new
                {
                    workDate = "2026-04-06",
                    client = "BudgetCo",
                    project = "BudgetProj",
                    task = "Week1",
                    hours = 2m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/timesheets/week/submit?weekStart={week1}", null)).StatusCode);

        var mgrToken = await LoginTokenAsync(client, "mgr.budget@local.test", "MgrBudget1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/timesheets/approvals/week/{icId}/approve?weekStart={week1}", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        await client.PutAsJsonAsync(
            $"/api/timesheets/week?weekStart={week2}",
            new[]
            {
                new
                {
                    workDate = "2026-04-13",
                    client = "BudgetCo",
                    project = "BudgetProj",
                    task = "Week2",
                    hours = 1m,
                    isBillable = true,
                    notes = (string?)null,
                },
            });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/timesheets/week/submit?weekStart={week2}", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);

        var review = await client.GetAsync($"/api/timesheets/approvals/week/{icId}/pending-review?weekStart={week2}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var body = await review.Content.ReadFromJsonAsync<PendingWeekReviewDto>();
        Assert.NotNull(body);
        Assert.NotNull(body!.ProjectBudgetBars);
        Assert.Single(body.ProjectBudgetBars!);
        var bar = body.ProjectBudgetBars[0];
        Assert.True(bar.CatalogMatched);
        Assert.Equal(10_000m, bar.BudgetAmount);
        Assert.Equal(100m, bar.DefaultHourlyRate);
        Assert.Equal(200m, bar.ConsumedBillableAmount);
        Assert.Equal(100m, bar.PendingSubmissionBillableAmount);
        Assert.Equal(1m, bar.PendingBillableHours);
    }

    private sealed record CreatedEntityIdDto(Guid Id);
}

