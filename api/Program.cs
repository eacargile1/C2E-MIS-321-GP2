using System.Security.Claims;
using System.Text;
using C2E.Api;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Middleware;
using C2E.Api.Models;
using C2E.Api.Dtos;
using C2E.Api.Options;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AiRecommendationOptions>(builder.Configuration.GetSection(AiRecommendationOptions.SectionName));
builder.Services.Configure<TimesheetWeekWindowOptions>(
    builder.Configuration.GetSection(TimesheetWeekWindowOptions.SectionName));
builder.Services.AddSingleton<TimesheetWeekWindow>();
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is required.");

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters (set via environment, user secrets, or appsettings.Development.json).");

if (jwt.AccessTokenMinutes < 1)
    throw new InvalidOperationException("Jwt:AccessTokenMinutes must be at least 1.");

var dbConnectivity = DatabaseConnectivity.Resolve(builder.Configuration);
builder.Services.AddAppDbContext(dbConnectivity, builder.Configuration);
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IStaffingRecommendationService, StaffingRecommendationService>();
builder.Services.AddHttpClient(nameof(OpenAiStaffingReranker));
builder.Services.AddScoped<IOpenAiStaffingReranker, OpenAiStaffingReranker>();

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

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 6 * 1024 * 1024;
});
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Length == 0)
    corsOrigins = ["http://localhost:5173"];
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Vite port changes, 127.0.0.1 vs localhost, etc. — all trigger opaque "failed to fetch" if not allowed.
            p.SetIsOriginAllowed(static origin =>
            {
                if (string.IsNullOrEmpty(origin)) return false;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                if (uri.Scheme is not ("http" or "https")) return false;
                return uri.Host is "localhost" or "127.0.0.1" or "[::1]";
            });
        }
        else
        {
            p.WithOrigins(corsOrigins);
        }

        p.AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (dbConnectivity.Kind == AppDatabaseKind.MySql)
{
    var pool = app.Configuration.GetValue("Database:MySqlMaxPoolSize", 4);
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    startupLog.LogInformation(
        "MySQL EF pool MaximumPoolSize={Pool}. If you still see max_user_connections, another process likely shares this DB user (second dotnet run, deployed site, Workbench) — stop extras or lower Database:MySqlMaxPoolSize.",
        pool);
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var log = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        if (ex != null)
            log.LogError(ex, "Unhandled exception");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        var msg = ex is null
            ? "Server error."
            : app.Environment.IsDevelopment()
                ? $"{ex.GetType().Name}: {ex.Message}"
                : "An unexpected error occurred.";
        if (app.Environment.IsDevelopment() && ex?.InnerException is { } ie)
            msg += $" | {ie.GetType().Name}: {ie.Message}";
        await context.Response.WriteAsJsonAsync(new AuthErrorResponse { Message = msg });
    });
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (dbConnectivity.Kind == AppDatabaseKind.InMemory)
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();

    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<AppUser>>();
    var seedEmail = app.Configuration["Seed:DevUserEmail"] ?? "dev@c2e.local";
    var seedPassword = app.Configuration["Seed:DevUserPassword"] ?? "ChangeMe!1";

    if (!await db.Users.AnyAsync())
        await DevRoleAccountsSeed.SeedWhenEmptyAsync(db, hasher, seedEmail, seedPassword);

    if (env.IsDevelopment() && cfg.GetValue("Seed:EnsureDevRoleAccounts", false))
        await DevRoleAccountsSeed.EnsureAdditionalDevRoleUsersAsync(db, hasher, seedPassword);
    if (env.IsDevelopment() && cfg.GetValue("Seed:DemoFinanceData", false))
        await DemoFinanceSeed.EnsureAsync(db, hasher);
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// CORS before HTTPS redirect so OPTIONS preflight is not redirected (browser shows "failed to fetch").
// In non-Development, enable HTTPS redirection after CORS.
app.UseCors();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<RequireActiveUserMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
