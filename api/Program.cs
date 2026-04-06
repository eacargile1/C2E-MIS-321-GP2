using System.Security.Claims;
using System.Text;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Middleware;
using C2E.Api.Models;
using C2E.Api.Options;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is required.");

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters (set via environment, user secrets, or appsettings.Development.json).");

if (jwt.AccessTokenMinutes < 1)
    throw new InvalidOperationException("Jwt:AccessTokenMinutes must be at least 1.");

var inMemoryName = builder.Configuration["Database:InMemoryName"] ?? "c2e-dev";
builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(inMemoryName));
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, JsonForbiddenAuthorizationMiddlewareResultHandler>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Length == 0)
    corsOrigins = ["http://localhost:5173"];
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        p.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<AppUser>>();
        var seedEmail = builder.Configuration["Seed:DevUserEmail"] ?? "dev@c2e.local";
        var seedPassword = builder.Configuration["Seed:DevUserPassword"] ?? "ChangeMe!1";
        var normalized = seedEmail.Trim().ToLowerInvariant();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            PasswordHash = "",
            Role = AppRole.Admin,
            IsActive = true,
        };
        user.PasswordHash = hasher.HashPassword(user, seedPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<RequireActiveUserMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
