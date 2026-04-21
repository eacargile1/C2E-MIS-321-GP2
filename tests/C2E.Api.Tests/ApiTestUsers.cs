using System.Net.Http.Json;
using C2E.Api.Data;

namespace C2E.Api.Tests;

/// <summary>Helpers for admin user API tests (dev seed includes <see cref="DevRoleAccountsSeed.DevManagerEmail"/>).</summary>
internal static class ApiTestUsers
{
    internal sealed record UserListRow(Guid Id, string Email, string Role);

    /// <summary>Caller must use an admin-authenticated client. GET /api/users triggers seed on empty DB.</summary>
    internal static async Task<Guid> SeededDevManagerIdAsync(HttpClient adminClient)
    {
        var list = await adminClient.GetFromJsonAsync<List<UserListRow>>("/api/users");
        var row = list!.FirstOrDefault(u =>
            string.Equals(u.Email, DevRoleAccountsSeed.DevManagerEmail, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            throw new InvalidOperationException($"Expected seed user {DevRoleAccountsSeed.DevManagerEmail}.");

        return row.Id;
    }

    /// <summary>Dev seed partner used as reporting partner for Manager/Finance in tests.</summary>
    internal static async Task<Guid> SeededDevPartnerIdAsync(HttpClient adminClient)
    {
        var list = await adminClient.GetFromJsonAsync<List<UserListRow>>("/api/users");
        var row = list!.FirstOrDefault(u =>
            string.Equals(u.Email, DevRoleAccountsSeed.DevPartnerEmail, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            throw new InvalidOperationException($"Expected seed user {DevRoleAccountsSeed.DevPartnerEmail}.");

        return row.Id;
    }

    /// <summary>
    /// After POST /api/users (IC bootstrap with org manager), set final role. Finance and Manager get
    /// <see cref="DevRoleAccountsSeed.DevPartnerEmail"/> as reporting partner (required when another manager exists).
    /// </summary>
    internal static async Task PatchUserRoleFromBootstrapIcAsync(HttpClient adminAuthedClient, Guid userId, string role)
    {
        if (string.Equals(role, "Finance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            var partnerId = await SeededDevPartnerIdAsync(adminAuthedClient);
            var patch = await adminAuthedClient.PatchAsJsonAsync(
                $"/api/users/{userId}",
                new { role, assignPartner = true, partnerUserId = partnerId });
            patch.EnsureSuccessStatusCode();
            return;
        }

        var p = await adminAuthedClient.PatchAsJsonAsync($"/api/users/{userId}", new { role });
        p.EnsureSuccessStatusCode();
    }
}
