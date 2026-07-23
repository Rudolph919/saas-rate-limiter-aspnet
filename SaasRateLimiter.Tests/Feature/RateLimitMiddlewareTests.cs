using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasRateLimiter.Tests.TestSupport;

namespace SaasRateLimiter.Tests.Feature;

public class RateLimitMiddlewareTests : ApiTestBase
{
    [Fact]
    public async Task MissingOrgHeaderReturns401()
    {
        var client = CreateClient();

        var response = await client.GetWithOrgAsync("/api/items");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("error").GetString());
        Assert.Equal("X-Org-Id header is required", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExemptHealthRouteWorksWithoutOrgHeader()
    {
        var client = CreateClient();

        var response = await client.GetWithOrgAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RequestUnderLimitSucceeds()
    {
        var client = CreateClient();

        var response = await client.GetWithOrgAsync("/api/items", "org_globex");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Widget", body.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostEndpointLimitDoesNotBlockGetForSameOrg()
    {
        var client = CreateClient(o =>
        {
            o.Tiers["standard"].MaxRequests = 500;
            o.EndpointLimits =
            [
                new() { Name = "read_items", Methods = ["GET", "HEAD"], Path = "api/items*", MaxRequests = 80 },
                new() { Name = "create_item", Methods = ["POST"], Path = "api/items*", MaxRequests = 2 },
            ];
        });

        Assert.Equal(HttpStatusCode.Created, (await client.PostWithOrgAsync("/api/items", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithOrgAsync("/api/items", "org_globex")).StatusCode);

        var blocked = await client.PostWithOrgAsync("/api/items", "org_globex");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("per_endpoint", blockedBody.GetProperty("limit").GetString());

        var read = await client.GetWithOrgAsync("/api/items", "org_globex");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, readBody.GetProperty("data")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task DeleteEndpointLimitReturns429()
    {
        var client = CreateClient(o =>
        {
            o.Tiers["standard"].MaxRequests = 500;
            o.EndpointLimits =
            [
                new() { Name = "delete_item", Methods = ["DELETE"], Path = "api/items*", MaxRequests = 2 },
            ];
        });

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteWithOrgAsync("/api/items/1", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteWithOrgAsync("/api/items/2", "org_globex")).StatusCode);

        var response = await client.DeleteWithOrgAsync("/api/items/3", "org_globex");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", body.GetProperty("error").GetString());
        Assert.Equal("per_endpoint", body.GetProperty("limit").GetString());
        Assert.Contains("delete_item", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RetryAfterHeaderMatchesResponseBody()
    {
        var client = CreateClient(o => o.Tiers["standard"].MaxRequests = 1);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);

        var response = await client.GetWithOrgAsync("/api/items", "org_globex");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var retryAfterSeconds = body.GetProperty("retry_after_seconds").GetInt32();
        Assert.Equal(retryAfterSeconds.ToString(), response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task PerClient429DetailNamesTier()
    {
        var client = CreateClient(o => o.Tiers["standard"].MaxRequests = 1);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);

        var response = await client.GetWithOrgAsync("/api/items", "org_globex");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("per_client", body.GetProperty("limit").GetString());

        var detail = body.GetProperty("detail").GetString();
        Assert.Contains("org_globex", detail);
        Assert.Contains("standard", detail);
    }

    [Fact]
    public async Task PerClientLimitReturns429()
    {
        var client = CreateClient(o => o.Tiers["standard"].MaxRequests = 2);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);

        var response = await client.GetWithOrgAsync("/api/items", "org_globex");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", body.GetProperty("error").GetString());
        Assert.Equal("per_client", body.GetProperty("limit").GetString());
        Assert.True(body.TryGetProperty("detail", out _));
        Assert.True(body.TryGetProperty("retry_after_seconds", out _));
    }

    [Fact]
    public async Task PerEndpointLimitReturns429OnPost()
    {
        var client = CreateClient(o =>
        {
            o.Tiers["standard"].MaxRequests = 100;
            o.EndpointLimits =
            [
                new() { Name = "create_item", Methods = ["POST"], Path = "api/items*", MaxRequests = 2 },
            ];
        });

        Assert.Equal(HttpStatusCode.Created, (await client.PostWithOrgAsync("/api/items", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithOrgAsync("/api/items", "org_globex")).StatusCode);

        var response = await client.PostWithOrgAsync("/api/items", "org_globex");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", body.GetProperty("error").GetString());
        Assert.Equal("per_endpoint", body.GetProperty("limit").GetString());
        Assert.Contains("create_item", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PremiumOrgHasHigherClientCeiling()
    {
        var client = CreateClient(o => o.Tiers["standard"].MaxRequests = 2);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetWithOrgAsync("/api/items", "org_globex")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_acme")).StatusCode);
    }

    [Fact]
    public async Task UnknownOrgGetsDefaultTierNotSharedUnknownBucket()
    {
        var client = CreateClient(o => o.Tiers["standard"].MaxRequests = 2);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_new_customer_a")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_new_customer_a")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetWithOrgAsync("/api/items", "org_new_customer_a")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetWithOrgAsync("/api/items", "org_new_customer_b")).StatusCode);
    }
}
