namespace SaasRateLimiter.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public string OrgHeader { get; set; } = "X-Org-Id";

    public string DefaultTier { get; set; } = "standard";

    public int WindowSeconds { get; set; } = 60;

    public Dictionary<string, TierOptions> Tiers { get; set; } = new();

    public Dictionary<string, string> Organizations { get; set; } = new();

    public List<string> Exempt { get; set; } = new();

    public List<EndpointLimitOptions> EndpointLimits { get; set; } = new();

    public EndpointLimitOptions DefaultEndpointLimit { get; set; } = new() { Name = "default", MaxRequests = 30 };
}

public sealed class TierOptions
{
    public string Label { get; set; } = string.Empty;

    public int MaxRequests { get; set; }
}

public sealed class EndpointLimitOptions
{
    public string Name { get; set; } = string.Empty;

    public List<string> Methods { get; set; } = new();

    public string Path { get; set; } = string.Empty;

    public int MaxRequests { get; set; }
}
