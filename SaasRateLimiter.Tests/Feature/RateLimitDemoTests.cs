using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaasRateLimiter.Tests.TestSupport;

namespace SaasRateLimiter.Tests.Feature;

public class RateLimitDemoTests : ApiTestBase
{
    [Fact]
    public async Task NoisyTenantPostLimitDoesNotBlockOtherTenantReads()
    {
        var client = CreateClient(o =>
        {
            o.Tiers["standard"].MaxRequests = 500;
            o.EndpointLimits =
            [
                new() { Name = "create_item", Methods = ["POST"], Path = "api/items*", MaxRequests = 3 },
                new() { Name = "read_items", Methods = ["GET", "HEAD"], Path = "api/items*", MaxRequests = 80 },
            ];
        });

        const string noisyTenant = "org_globex";
        const string otherTenant = "org_acme";

        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostWithOrgAsync("/api/items", noisyTenant);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var blocked = await client.PostWithOrgAsync("/api/items", noisyTenant);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", blockedBody.GetProperty("error").GetString());
        Assert.Equal("per_endpoint", blockedBody.GetProperty("limit").GetString());

        var otherRead = await client.GetWithOrgAsync("/api/items", otherTenant);
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        var otherBody = await otherRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Widget", otherBody.GetProperty("data")[0].GetProperty("name").GetString());
    }
}
