using Microsoft.Extensions.Options;
using SaasRateLimiter.Configuration;
using SaasRateLimiter.Services.RateLimiting;

namespace SaasRateLimiter.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _orgHeader;
    private readonly int _windowSeconds;

    public RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
    {
        _next = next;
        _orgHeader = options.Value.OrgHeader;
        _windowSeconds = options.Value.WindowSeconds;
    }

    public async Task InvokeAsync(HttpContext context, RateLimitResolver resolver, RateLimitCounter counter)
    {
        var resolution = resolver.Resolve(context.Request);

        if (resolution.Exempt)
        {
            await _next(context);
            return;
        }

        if (resolution.MissingOrg)
        {
            await WriteMissingOrgResponse(context);
            return;
        }

        foreach (var limit in resolution.Limits)
        {
            var result = counter.Attempt(limit);

            if (!result.Allowed)
            {
                await WriteRateLimitResponse(context, result, resolution);
                return;
            }
        }

        await _next(context);
    }

    private async Task WriteMissingOrgResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await context.Response.WriteAsJsonAsync(new
        {
            error = "unauthorized",
            detail = $"{_orgHeader} header is required",
        });
    }

    private async Task WriteRateLimitResponse(HttpContext context, RateLimitResult result, RateLimitResolution resolution)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();

        await context.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            limit = result.Type,
            detail = BuildDetailMessage(result, resolution),
            retry_after_seconds = result.RetryAfterSeconds,
        });
    }

    private string BuildDetailMessage(RateLimitResult result, RateLimitResolution resolution)
    {
        var orgId = resolution.OrgId;

        return result.Type == "per_client"
            ? $"Organization {orgId} exceeded {result.MaxRequests} requests per {_windowSeconds} seconds ({result.Name} tier)"
            : $"Organization {orgId} exceeded {result.MaxRequests} requests per {_windowSeconds} seconds on {result.Name} endpoint";
    }
}
