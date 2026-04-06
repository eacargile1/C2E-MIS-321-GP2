using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace C2E.Api.Tests;

public class AuthLoginTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:InMemoryName", $"auth-tests-{Guid.NewGuid():N}");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-must-be-32-chars-min!!");
            b.UseSetting("Seed:DevUserEmail", "test@local.test");
            b.UseSetting("Seed:DevUserPassword", "TestPass!9");
        });

    [Fact]
    public async Task Login_valid_credentials_returns_bearer_jwt()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@local.test", password = "TestPass!9" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.False(string.IsNullOrEmpty(body?.AccessToken));
        Assert.Equal("Bearer", body.TokenType);
        Assert.True(body.ExpiresInSeconds > 0);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401_generic_message()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@local.test", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid email or password.", err?.Message);
    }

    [Fact]
    public async Task Login_unknown_email_returns_same_generic_message()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@local.test", password = "TestPass!9" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid email or password.", err?.Message);
    }

    [Fact]
    public async Task Me_with_valid_token_returns_profile()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@local.test", password = "TestPass!9" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("test@local.test", profile?.Email);
        Assert.NotEqual(Guid.Empty, profile?.Id);
        Assert.Equal("Admin", profile?.Role);
        Assert.True(profile?.IsActive);
    }

    [Fact]
    public async Task Me_with_token_after_account_deactivated_returns_401_with_same_message_as_login()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@local.test", password = "TestPass!9" });
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "ic.tok@local.test", password = "IcTokPass1!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        client.DefaultRequestHeaders.Authorization = null;
        var icLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "ic.tok@local.test", password = "IcTokPass1!" });
        var icToken = (await icLogin.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await client.PatchAsJsonAsync($"/api/users/{created!.Id}", new { isActive = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", icToken);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        var err = await me.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid email or password.", err?.Message);
    }

    [Fact]
    public async Task Login_inactive_user_returns_same_generic_message_as_wrong_password()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        var adminLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@local.test", password = "TestPass!9" });
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new { email = "inactive@local.test", password = "InactiveP1!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        await client.PatchAsJsonAsync(
            $"/api/users/{created!.Id}",
            new { isActive = false });
        client.DefaultRequestHeaders.Authorization = null;
        var res = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "inactive@local.test", password = "InactiveP1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var err = await res.Content.ReadFromJsonAsync<AuthErrorDto>();
        Assert.Equal("Invalid email or password.", err?.Message);
    }

    private sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresInSeconds);

    private sealed record AuthErrorDto(string Message);

    private sealed record MeDto(Guid Id, string Email, string Role, bool IsActive);

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
}
