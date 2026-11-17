using System.Security.Claims;
using LeadPilot.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LeadPilot.API.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(User?.FindFirstValue("sub") ?? User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? Role => User?.FindFirstValue(ClaimTypes.Role) ?? User?.FindFirstValue("role");
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public Guid RequireUserId() => UserId ?? throw new UnauthorizedException("Authentication required.");
}

/// <summary>Maps exceptions to RFC 7807 problem details without leaking internals.</summary>
public sealed class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationFailedException v:
                problem = new ValidationProblemDetails(v.Errors) { Status = 400, Title = v.Message, Type = "validation_failed" };
                break;
            case FluentValidation.ValidationException fv:
                problem = new ValidationProblemDetails(fv.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
                    { Status = 400, Title = "Validation failed", Type = "validation_failed" };
                break;
            case AppException a:
                problem = new ProblemDetails { Status = a.StatusCode, Title = a.Message, Type = a.Code };
                break;
            case BadHttpRequestException b:
                problem = new ProblemDetails { Status = b.StatusCode, Title = "Bad request", Type = "bad_request" };
                break;
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return true;
            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                problem = new ProblemDetails { Status = 409, Title = "The record was changed by another operation. Reload and try again.", Type = "concurrency_conflict" };
                break;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred.", Type = "server_error", Detail = env.IsDevelopment() ? exception.Message : null };
                break;
        }
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? 500;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}

public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (!ctx.Request.Path.StartsWithSegments("/swagger"))
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; style-src 'unsafe-inline'";
        await next();
    });
}
