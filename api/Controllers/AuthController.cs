using System.Security.Claims;
using C2E.Api;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Options;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    AppDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    IJwtTokenService jwt,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest? body, CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new AuthErrorResponse { Message = "Request body is required." });

        if (!ModelState.IsValid)
            return Unauthorized(new AuthErrorResponse { Message = AuthMessages.InvalidCredentials });

        try
        {
            var normalized = body.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
            if (user is null)
                return Unauthorized(new AuthErrorResponse { Message = AuthMessages.InvalidCredentials });

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
            if (verify == PasswordVerificationResult.Failed)
                return Unauthorized(new AuthErrorResponse { Message = AuthMessages.InvalidCredentials });

            if (!user.IsActive)
                return Unauthorized(new AuthErrorResponse { Message = AuthMessages.InvalidCredentials });

            if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, body.Password);
                await db.SaveChangesAsync(ct);
            }

            var accessToken = jwt.CreateAccessToken(user);
            var minutes = Math.Max(1, jwtOptions.Value.AccessTokenMinutes);
            return Ok(new LoginResponse
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ExpiresInSeconds = minutes * 60,
            });
        }
        catch (Exception ex) when (IsMySqlConnectionSaturation(ex))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new AuthErrorResponse
                {
                    Message =
                        "Database is at its connection limit—wait a few seconds and try again. If this persists, ask your host to raise max_user_connections or reduce concurrent app instances.",
                });
        }
    }

    private static bool IsMySqlConnectionSaturation(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is MySqlException mx &&
                mx.Message.Contains("max_user_connections", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name);
        if (sub is null || !Guid.TryParse(sub, out var id))
            return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.IsActive)
            return Unauthorized();

        return Ok(new MeResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? UserProfileName.DefaultFromEmail(user.Email)
                : user.DisplayName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
        });
    }
}
