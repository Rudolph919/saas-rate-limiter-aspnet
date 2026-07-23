namespace SaasRateLimiter.Services.RateLimiting;

public sealed record RateLimitResult(
    bool Allowed,
    string Key,
    string Type,
    string Name,
    int MaxRequests,
    int CurrentCount,
    int RetryAfterSeconds);
