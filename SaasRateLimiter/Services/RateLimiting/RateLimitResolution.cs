namespace SaasRateLimiter.Services.RateLimiting;

public sealed record RateLimitResolution(
    bool Exempt,
    bool MissingOrg,
    string? OrgId,
    string? Tier,
    IReadOnlyList<ResolvedLimit> Limits);
