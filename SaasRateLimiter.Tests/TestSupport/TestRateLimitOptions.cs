using SaasRateLimiter.Configuration;

namespace SaasRateLimiter.Tests.TestSupport;

/// <summary>Mirrors the defaults in appsettings.json / config/rate_limits.php for unit tests.</summary>
internal static class TestRateLimitOptions
{
    public static RateLimitOptions Default() => new()
    {
        OrgHeader = "X-Org-Id",
        DefaultTier = "standard",
        WindowSeconds = 60,
        Tiers = new Dictionary<string, TierOptions>
        {
            ["standard"] = new() { Label = "Standard", MaxRequests = 100 },
            ["premium"] = new() { Label = "Premium", MaxRequests = 500 },
        },
        Organizations = new Dictionary<string, string>
        {
            ["org_acme"] = "premium",
            ["org_globex"] = "standard",
            ["org_initech"] = "standard",
        },
        Exempt = ["api/health"],
        EndpointLimits =
        [
            new() { Name = "read_items", Methods = ["GET", "HEAD"], Path = "api/items*", MaxRequests = 80 },
            new() { Name = "create_item", Methods = ["POST"], Path = "api/items*", MaxRequests = 20 },
            new() { Name = "update_item", Methods = ["PUT", "PATCH"], Path = "api/items*", MaxRequests = 20 },
            new() { Name = "delete_item", Methods = ["DELETE"], Path = "api/items*", MaxRequests = 10 },
        ],
        DefaultEndpointLimit = new EndpointLimitOptions { Name = "default", MaxRequests = 30 },
    };
}
