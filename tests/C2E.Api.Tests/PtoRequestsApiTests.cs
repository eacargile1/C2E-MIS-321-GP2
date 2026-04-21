using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using C2E.Api;
using C2E.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C2E.Api.Tests;

public class PtoRequestsApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"pto-tests-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task IC_pto_pending_manager_approves()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var mgrId = await ApiTestUsers.SeededDevManagerIdAsync(client);
        var createIc = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "ic.pto@local.test", password = "IcPtoPass1!", managerUserId = mgrId });
        createIc.EnsureSuccessStatusCode();

        var icToken = await LoginTokenAsync(client, "ic.pto@local.test", "IcPtoPass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var created = await client.PostAsJsonAsync(
            "/api/pto-requests",
            new { startDate = "2026-05-01", endDate = "2026-05-02", reason = "test" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = await created.Content.ReadFromJsonAsync<PtoRowDto>();
        Assert.NotNull(row);
        Assert.Equal("Pending", row!.Status);

        var mgrToken = await LoginTokenAsync(client, DevRoleAccountsSeed.DevManagerEmail, "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var pending = await client.GetFromJsonAsync<List<PtoRowDto>>("/api/pto-requests/pending-approval");
        Assert.Contains(pending!, p => p.Id == row.Id);

        var ok = await client.PostAsync($"/api/pto-requests/{row.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task IC_pto_pending_partner_sees_queue_when_ic_has_reporting_partner()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var mgrId = await ApiTestUsers.SeededDevManagerIdAsync(client);
        var partnerId = await ApiTestUsers.SeededDevPartnerIdAsync(client);
        var createIc = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "ic.pto2@local.test", password = "IcPto2Pass1!", managerUserId = mgrId });
        createIc.EnsureSuccessStatusCode();
        var icRow = await createIc.Content.ReadFromJsonAsync<CreatedUserDto>();
        Assert.NotNull(icRow);

        var patch = await client.PatchAsJsonAsync(
            $"/api/users/{icRow!.Id}",
            new { assignPartner = true, partnerUserId = partnerId });
        patch.EnsureSuccessStatusCode();

        var icToken = await LoginTokenAsync(client, "ic.pto2@local.test", "IcPto2Pass1!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);
        var created = await client.PostAsJsonAsync(
            "/api/pto-requests",
            new { startDate = "2026-05-10", endDate = "2026-05-11", reason = "dual" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = await created.Content.ReadFromJsonAsync<PtoRowDto>();
        Assert.NotNull(row);
        Assert.Equal("Pending", row!.Status);

        var parToken = await LoginTokenAsync(client, DevRoleAccountsSeed.DevPartnerEmail, "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parToken);
        var pendingPar = await client.GetFromJsonAsync<List<PtoRowDto>>("/api/pto-requests/pending-approval");
        Assert.Contains(pendingPar!, p => p.Id == row.Id);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginTokenAsync(client, DevRoleAccountsSeed.DevManagerEmail, "AdminPass!9"));
        var pendingMgr = await client.GetFromJsonAsync<List<PtoRowDto>>("/api/pto-requests/pending-approval");
        Assert.Contains(pendingMgr!, p => p.Id == row.Id);
    }

    [Fact]
    public async Task Partner_pto_auto_approved()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var parToken = await LoginTokenAsync(client, DevRoleAccountsSeed.DevPartnerEmail, "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parToken);
        var created = await client.PostAsJsonAsync(
            "/api/pto-requests",
            new { startDate = "2026-06-01", endDate = "2026-06-02" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = await created.Content.ReadFromJsonAsync<PtoRowDto>();
        Assert.Equal("Approved", row!.Status);
    }

    [Fact]
    public async Task Approved_pto_shows_PTO_status_on_organization_month()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var parToken = await LoginTokenAsync(client, DevRoleAccountsSeed.DevPartnerEmail, "AdminPass!9");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parToken);
        var created = await client.PostAsJsonAsync(
            "/api/pto-requests",
            new { startDate = "2026-06-03", endDate = "2026-06-04" });
        created.EnsureSuccessStatusCode();
        var row = await created.Content.ReadFromJsonAsync<PtoRowDto>();
        Assert.Equal("Approved", row!.Status);

        var month = await client.GetFromJsonAsync<List<OrgMatrixRowDto>>("/api/timesheets/organization?monthStart=2026-06-01");
        var partnerRow = month!.First(r =>
            string.Equals(r.Email, DevRoleAccountsSeed.DevPartnerEmail, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("PTO", partnerRow.Days.First(d => d.Date == "2026-06-03").Status);
        Assert.Equal("PTO", partnerRow.Days.First(d => d.Date == "2026-06-04").Status);
        Assert.Equal("Available", partnerRow.Days.First(d => d.Date == "2026-06-05").Status);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record CreatedUserDto([property: JsonPropertyName("id")] Guid Id);

    private sealed record PtoRowDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("status")] string Status);

    private sealed record OrgMatrixRowDto(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("days")] List<OrgMatrixDayDto> Days);

    private sealed record OrgMatrixDayDto(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("hours")] decimal Hours);
}
