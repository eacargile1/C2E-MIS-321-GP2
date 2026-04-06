using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using C2E.Api;
using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace C2E.Api.Tests;

public class UsersAdminTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"users-tests-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "admin@local.test");
            b.UseSetting("Seed:DevUserPassword", "AdminPass!9");
        });

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "admin@local.test", password = "AdminPass!9" });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
    }

    [Fact]
    public async Task Users_list_as_admin_returns_seed_and_created_users()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));

        var list = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var users = await list.Content.ReadFromJsonAsync<List<UserDto>>();
        Assert.NotNull(users);
        Assert.Contains(users, u => u.Email == "admin@local.test" && u.Role == "Admin" && u.IsActive);

        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "new.ic@local.test", password = "NewUserP1!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("IC", created?.Role);
        Assert.True(created?.IsActive);

        var list2 = await client.GetFromJsonAsync<List<UserDto>>("/api/users");
        Assert.Contains(list2!, u => u.Email == "new.ic@local.test");
    }

    [Fact]
    public async Task Users_IC_token_receives_403_with_json_message()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await client.PostAsJsonAsync(
            "/api/users",
            new { email = "ic.only@local.test", password = "IcOnlyPa1!" });
        client.DefaultRequestHeaders.Authorization = null;

        var icLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "ic.only@local.test", password = "IcOnlyPa1!" });
        var icToken = (await icLogin.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        var res = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal(AuthMessages.Forbidden, err?.Message);
    }

    [Fact]
    public async Task Users_patch_email_and_password_as_admin()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "patch.me@local.test", password = "Original1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var patch = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { email = "patched@local.test", password = "UpdatedP1!" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var updated = await patch.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("patched@local.test", updated?.Email);

        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "patched@local.test", password = "UpdatedP1!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Deactivated_admin_gets_401_on_users_api_with_stale_token()
    {
        using var factory = Factory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<AppUser>>();
            var u = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "admin2@local.test",
                PasswordHash = "",
                Role = AppRole.Admin,
                IsActive = true,
            };
            u.PasswordHash = hasher.HashPassword(u, "AdminTwoP1!");
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var login1 = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "admin@local.test", password = "AdminPass!9" });
        var token1 = (await login1.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var me1 = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        client.DefaultRequestHeaders.Authorization = null;

        var login2 = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "admin2@local.test", password = "AdminTwoP1!" });
        var token2 = (await login2.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var patch = await client.PatchAsJsonAsync(
            $"/api/users/{me1!.Id}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var list = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        var err = await list.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid email or password.", err?.Message);
    }

    [Fact]
    public async Task Users_cannot_deactivate_last_active_admin()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        var res = await client.PatchAsJsonAsync(
            $"/api/users/{me!.Id}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Users_patch_role_IC_to_Manager_then_Finance()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "role.chain@local.test", password = "RoleChain1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var p1 = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "Manager" });
        Assert.Equal(HttpStatusCode.OK, p1.StatusCode);
        var u1 = await p1.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Manager", u1?.Role);

        var p2 = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "Finance" });
        Assert.Equal(HttpStatusCode.OK, p2.StatusCode);
        var u2 = await p2.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Finance", u2?.Role);
    }

    [Fact]
    public async Task Users_patch_role_case_insensitive_returns_canonical_names()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "case.role@local.test", password = "CaseRoleP1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var p1 = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "manager" });
        Assert.Equal(HttpStatusCode.OK, p1.StatusCode);
        var u1 = await p1.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Manager", u1?.Role);

        var p2 = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "FINANCE" });
        Assert.Equal(HttpStatusCode.OK, p2.StatusCode);
        var u2 = await p2.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Finance", u2?.Role);
    }

    [Fact]
    public async Task Users_patch_empty_body_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "empty.patch@local.test", password = "EmptyPat1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var res = await client.PatchAsync(
            $"/api/users/{created.Id}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Provide at least one of email, password, isActive, or role.", err?.Message);
    }

    [Fact]
    public async Task Users_patch_numeric_role_string_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "numeric.role@local.test", password = "NumericRo1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var res = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "0" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid role. Use IC, Admin, Manager, or Finance.", err?.Message);
    }

    [Fact]
    public async Task Users_patch_invalid_role_returns_400()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "bad.role@local.test", password = "BadRolePa1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        var res = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "NotARole" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid role. Use IC, Admin, Manager, or Finance.", err?.Message);
    }

    [Fact]
    public async Task Users_patch_role_as_IC_receives_403()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminToken = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await client.PostAsJsonAsync(
            "/api/users",
            new { email = "ic.patch@local.test", password = "IcPatchP1!" });
        var target = await client.GetFromJsonAsync<List<UserDto>>("/api/users");
        var adminId = target!.First(u => u.Email == "admin@local.test").Id;

        client.DefaultRequestHeaders.Authorization = null;
        var icLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "ic.patch@local.test", password = "IcPatchP1!" });
        var icToken = (await icLogin.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        var res = await client.PatchAsJsonAsync(
            $"/api/users/{adminId}",
            new { role = "Manager" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal(AuthMessages.Forbidden, err?.Message);
    }

    [Fact]
    public async Task Users_patch_promote_IC_to_Admin_when_second_admin_exists()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "promote.admin@local.test", password = "PromoteAd1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("IC", created.Role);

        var patch = await client.PatchAsJsonAsync(
            $"/api/users/{created.Id}",
            new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var updated = await patch.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Admin", updated?.Role);
    }

    [Fact]
    public async Task Users_cannot_demote_last_active_admin_via_role()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        var res = await client.PatchAsJsonAsync(
            $"/api/users/{me!.Id}",
            new { role = "IC" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Cannot demote the last active administrator.", err?.Message);
    }

    [Fact]
    public async Task Me_after_login_reflects_patched_role()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client));
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "me.role@local.test", password = "MeRolePa1!" });
        var created = (await create.Content.ReadFromJsonAsync<UserDto>())!;

        await client.PatchAsJsonAsync($"/api/users/{created.Id}", new { role = "Manager" });

        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "me.role@local.test", password = "MeRolePa1!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal("Manager", me?.Role);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record AuthErrorDto(string Message);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record MeDto(Guid Id, string Email, string Role, bool IsActive);
}
