using Microsoft.AspNetCore.Http;
using SaasRateLimiter.Services.RateLimiting;
using SaasRateLimiter.Tests.TestSupport;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace SaasRateLimiter.Tests.Unit.RateLimiting;

public class RateLimitResolverTests
{
    private readonly RateLimitResolver _resolver = new(MEOptions.Create(TestRateLimitOptions.Default()));

    [Fact]
    public void ExemptPathSkipsLimitsWithoutOrgHeader()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/health"));

        Assert.True(resolution.Exempt);
        Assert.False(resolution.MissingOrg);
        Assert.Null(resolution.OrgId);
        Assert.Empty(resolution.Limits);
    }

    [Fact]
    public void ExemptPathStillResolvesOrgWhenPresent()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/health", "org_acme"));

        Assert.True(resolution.Exempt);
        Assert.Equal("org_acme", resolution.OrgId);
        Assert.Equal("premium", resolution.Tier);
    }

    [Fact]
    public void PremiumOrgGetsHigherClientLimit()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/items", "org_acme"));

        Assert.False(resolution.Exempt);
        Assert.Equal("org_acme", resolution.OrgId);
        Assert.Equal("premium", resolution.Tier);
        Assert.Equal(500, resolution.Limits[0].MaxRequests);
        Assert.Equal("per_client", resolution.Limits[0].Type);
        Assert.Equal("client:org_acme", resolution.Limits[0].Key);
    }

    [Fact]
    public void UnknownOrgUsesDefaultTierWithOwnCounterKey()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/items", "org_unknown"));

        Assert.False(resolution.MissingOrg);
        Assert.Equal("org_unknown", resolution.OrgId);
        Assert.Equal("standard", resolution.Tier);
        Assert.Equal(100, resolution.Limits[0].MaxRequests);
        Assert.Equal("client:org_unknown", resolution.Limits[0].Key);
    }

    [Fact]
    public void MissingOrgHeaderFlagsRejection()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/items"));

        Assert.False(resolution.Exempt);
        Assert.True(resolution.MissingOrg);
        Assert.Null(resolution.OrgId);
        Assert.Null(resolution.Tier);
        Assert.Empty(resolution.Limits);
    }

    [Fact]
    public void GetItemsUsesReadEndpointLimit()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/items", "org_globex"));
        var endpointLimit = resolution.Limits[1];

        Assert.Equal("per_endpoint", endpointLimit.Type);
        Assert.Equal("read_items", endpointLimit.Name);
        Assert.Equal(80, endpointLimit.MaxRequests);
        Assert.Equal("endpoint:org_globex:read_items", endpointLimit.Key);
    }

    [Fact]
    public void PostItemsUsesStricterWriteLimit()
    {
        var resolution = _resolver.Resolve(CreateRequest("POST", "/api/items", "org_globex"));

        Assert.Equal("create_item", resolution.Limits[1].Name);
        Assert.Equal(20, resolution.Limits[1].MaxRequests);
    }

    [Fact]
    public void DeleteItemWithIdMatchesDeleteRule()
    {
        var resolution = _resolver.Resolve(CreateRequest("DELETE", "/api/items/42", "org_globex"));

        Assert.Equal("delete_item", resolution.Limits[1].Name);
        Assert.Equal(10, resolution.Limits[1].MaxRequests);
    }

    [Fact]
    public void UnlistedRouteUsesDefaultEndpointLimit()
    {
        var resolution = _resolver.Resolve(CreateRequest("GET", "/api/reports", "org_globex"));

        Assert.Equal("default", resolution.Limits[1].Name);
        Assert.Equal(30, resolution.Limits[1].MaxRequests);
        Assert.Equal("endpoint:org_globex:default", resolution.Limits[1].Key);
    }

    [Theory]
    [InlineData("api/items", "api/items", true)]
    [InlineData("api/items/42", "api/items*", true)]
    [InlineData("api/items", "api/items*", true)]
    [InlineData("api/items-archive", "api/items*", false)]
    [InlineData("api/users", "api/items*", false)]
    [InlineData("api/health", "api/health", true)]
    public void PathMatching(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, _resolver.PathMatches(path, pattern));
    }

    private static HttpRequest CreateRequest(string method, string path, string? orgId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (orgId is not null)
        {
            context.Request.Headers["X-Org-Id"] = orgId;
        }

        return context.Request;
    }
}
