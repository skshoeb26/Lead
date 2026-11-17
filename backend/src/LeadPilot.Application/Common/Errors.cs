namespace LeadPilot.Application.Common;

/// <summary>Base for expected, user-facing errors. Mapped to RFC 7807 problem responses by the API.</summary>
public abstract class AppException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string entity, object id)
    : AppException($"{entity} '{id}' was not found.", "not_found")
{
    public override int StatusCode => 404;
}

public sealed class ValidationFailedException(string message, IDictionary<string, string[]>? errors = null)
    : AppException(message, "validation_failed")
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public override int StatusCode => 400;
}

public sealed class ConflictException(string message, string code = "conflict") : AppException(message, code)
{
    public override int StatusCode => 409;
}

public sealed class ForbiddenException(string message, string code = "forbidden") : AppException(message, code)
{
    public override int StatusCode => 403;
}

public sealed class UnauthorizedException(string message, string code = "unauthorized") : AppException(message, code)
{
    public override int StatusCode => 401;
}

/// <summary>A required external integration has not been configured (missing credentials).</summary>
public sealed class IntegrationNotConfiguredException(string provider, string hint)
    : AppException($"{provider} is not configured. {hint}", "integration_not_configured")
{
    public string Provider { get; } = provider;
    public override int StatusCode => 503;
}

public sealed class BudgetExceededException(string message) : AppException(message, "budget_exceeded")
{
    public override int StatusCode => 402;
}

public sealed class RateLimitedException(string message) : AppException(message, "rate_limited")
{
    public override int StatusCode => 429;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
