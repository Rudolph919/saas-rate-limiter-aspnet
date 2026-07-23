namespace SaasRateLimiter.Tests.TestSupport;

internal static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> GetWithOrgAsync(this HttpClient client, string url, string? orgId = null) =>
        client.SendAsync(BuildRequest(HttpMethod.Get, url, orgId));

    public static Task<HttpResponseMessage> PostWithOrgAsync(this HttpClient client, string url, string? orgId = null) =>
        client.SendAsync(BuildRequest(HttpMethod.Post, url, orgId));

    public static Task<HttpResponseMessage> DeleteWithOrgAsync(this HttpClient client, string url, string? orgId = null) =>
        client.SendAsync(BuildRequest(HttpMethod.Delete, url, orgId));

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string? orgId)
    {
        var request = new HttpRequestMessage(method, url);

        if (orgId is not null)
        {
            request.Headers.Add("X-Org-Id", orgId);
        }

        return request;
    }
}
