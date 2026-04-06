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
}

