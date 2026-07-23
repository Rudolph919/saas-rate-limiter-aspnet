using Microsoft.Extensions.Options;
using SaasRateLimiter.Configuration;

namespace SaasRateLimiter.Services.RateLimiting;

public sealed class RateLimitResolver
{
    private readonly RateLimitOptions _options;

    public RateLimitResolver(IOptions<RateLimitOptions> options)
    {
        _options = options.Value;
    }

    public RateLimitResolution Resolve(HttpRequest request)
    {
        var path = NormalizePath(request.Path.Value ?? string.Empty);

        if (IsExempt(path))
        {
            var exemptOrgId = ExtractOrgId(request);

            return new RateLimitResolution(
                Exempt: true,
                MissingOrg: false,
                OrgId: exemptOrgId,
                Tier: exemptOrgId is not null ? ResolveTier(exemptOrgId) : null,
                Limits: Array.Empty<ResolvedLimit>());
        }

        var orgId = ExtractOrgId(request);

        if (orgId is null)
        {
            return new RateLimitResolution(false, MissingOrg: true, null, null, Array.Empty<ResolvedLimit>());
        }

        var tier = ResolveTier(orgId);
        var windowSeconds = _options.WindowSeconds;

        var limits = new[]
        {
            ResolveClientLimit(orgId, tier, windowSeconds),
            ResolveEndpointLimit(request, orgId, path, windowSeconds),
        };

        return new RateLimitResolution(false, false, orgId, tier, limits);
    }

    public string? ExtractOrgId(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(_options.OrgHeader, out var values))
        {
            return null;
        }

        var orgId = values.ToString();

        return string.IsNullOrEmpty(orgId) ? null : orgId;
    }

    public string ResolveTier(string orgId)
    {
        var tier = _options.Organizations.GetValueOrDefault(orgId, _options.DefaultTier);

        return _options.Tiers.ContainsKey(tier) ? tier : _options.DefaultTier;
    }

    public bool PathMatches(string path, string pattern)
    {
        path = NormalizePath(path);
        pattern = NormalizePath(pattern);

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern.TrimEnd('*');

            return prefix.Length == 0
                || path == prefix
                || path.StartsWith(prefix + "/", StringComparison.Ordinal);
        }

        return path == pattern;
    }

    private bool IsExempt(string path) => _options.Exempt.Any(exemptPath => PathMatches(path, exemptPath));

    private ResolvedLimit ResolveClientLimit(string orgId, string tier, int windowSeconds)
    {
        var maxRequests = _options.Tiers.TryGetValue(tier, out var tierConfig) ? tierConfig.MaxRequests : 100;

        return new ResolvedLimit($"client:{orgId}", "per_client", tier, maxRequests, windowSeconds);
    }

    private ResolvedLimit ResolveEndpointLimit(HttpRequest request, string orgId, string path, int windowSeconds)
    {
        var method = request.Method.ToUpperInvariant();
        var rule = FindEndpointRule(method, path);

        if (rule is null)
        {
            var fallback = _options.DefaultEndpointLimit;

            return new ResolvedLimit($"endpoint:{orgId}:default", "per_endpoint", fallback.Name, fallback.MaxRequests, windowSeconds);
        }

        return new ResolvedLimit($"endpoint:{orgId}:{rule.Name}", "per_endpoint", rule.Name, rule.MaxRequests, windowSeconds);
    }

    private EndpointLimitOptions? FindEndpointRule(string method, string path) =>
        _options.EndpointLimits.FirstOrDefault(rule => rule.Methods.Contains(method) && PathMatches(path, rule.Path));

    private static string NormalizePath(string path) => path.Trim('/');
}
