using System.Security.Claims;
using C2E.Api.Data;
using C2E.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Middleware;

public sealed class RequireActiveUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue(ClaimTypes.Name);
        if (sub is null || !Guid.TryParse(sub, out var id))
        {
            await WriteJsonUnauthorizedAsync(context);
            return;
        }

        var allowed = await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive, context.RequestAborted);
        if (!allowed)
        {
            await WriteJsonUnauthorizedAsync(context);
            return;
        }

        await next(context);
    }

    private static async Task WriteJsonUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new AuthErrorResponse { Message = AuthMessages.InvalidCredentials });
    }
}
