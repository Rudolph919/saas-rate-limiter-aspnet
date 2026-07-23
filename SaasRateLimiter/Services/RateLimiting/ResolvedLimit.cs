namespace SaasRateLimiter.Services.RateLimiting;

public sealed record ResolvedLimit(
    string Key,
    string Type,
    string Name,
    int MaxRequests,
    int WindowSeconds);
