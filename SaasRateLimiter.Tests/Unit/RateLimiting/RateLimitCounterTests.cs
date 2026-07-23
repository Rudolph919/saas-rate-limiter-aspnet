using SaasRateLimiter.Services.RateLimiting;

namespace SaasRateLimiter.Tests.Unit.RateLimiting;

public class RateLimitCounterTests
{
    private readonly RateLimitCounter _counter = new();

    [Fact]
    public void FirstRequestIsAllowed()
    {
        var limit = MakeLimit(maxRequests: 100);

        var result = _counter.Attempt(limit, At(1_700_000_000));

        Assert.True(result.Allowed);
        Assert.Equal(1, result.CurrentCount);
        Assert.Equal("per_client", result.Type);
        Assert.Equal("client:org_acme", result.Key);
    }

    [Fact]
    public void RequestAtMaxLimitIsAllowed()
    {
        var limit = MakeLimit(maxRequests: 3);
        var now = At(1_700_000_000);

        for (var i = 0; i < 2; i++)
        {
            _counter.Attempt(limit, now);
        }

        var result = _counter.Attempt(limit, now);

        Assert.True(result.Allowed);
        Assert.Equal(3, result.CurrentCount);
    }

    [Fact]
    public void RequestOverMaxIsRejected()
    {
        var limit = MakeLimit(maxRequests: 3);
        var now = At(1_700_000_000);

        for (var i = 0; i < 3; i++)
        {
            _counter.Attempt(limit, now);
        }

        var result = _counter.Attempt(limit, now);

        Assert.False(result.Allowed);
        Assert.Equal(4, result.CurrentCount);
        Assert.Equal("standard", result.Name);
    }

    [Theory]
    [InlineData(1_700_000_039, 1)]
    [InlineData(1_700_000_010, 30)]
    public void RetryAfterIsSecondsUntilWindowEnds(long now, int expectedRetryAfter)
    {
        var limit = MakeLimit(maxRequests: 1, windowSeconds: 60);
        const long windowStart = 1_699_999_980;

        _counter.Attempt(limit, At(windowStart));
        var result = _counter.Attempt(limit, At(now));

        Assert.False(result.Allowed);
        Assert.Equal(expectedRetryAfter, result.RetryAfterSeconds);
    }

    [Fact]
    public void CounterResetsWhenWindowRollsOver()
    {
        var limit = MakeLimit(maxRequests: 2);
        const long windowOne = 1_699_999_980;
        const long windowTwo = 1_700_000_040;

        _counter.Attempt(limit, At(windowOne));
        _counter.Attempt(limit, At(windowOne));

        var rejected = _counter.Attempt(limit, At(windowOne));
        Assert.False(rejected.Allowed);

        var allowed = _counter.Attempt(limit, At(windowTwo));
        Assert.True(allowed.Allowed);
        Assert.Equal(1, allowed.CurrentCount);
    }

    [Fact]
    public void SeparateKeysAreIndependent()
    {
        var clientLimit = MakeLimit(key: "client:org_a", maxRequests: 1);
        var endpointLimit = MakeLimit(key: "endpoint:org_a:read_items", type: "per_endpoint", name: "read_items", maxRequests: 1);
        var now = At(1_700_000_000);

        _counter.Attempt(clientLimit, now);
        var clientRejected = _counter.Attempt(clientLimit, now);

        var endpointAllowed = _counter.Attempt(endpointLimit, now);

        Assert.False(clientRejected.Allowed);
        Assert.True(endpointAllowed.Allowed);
        Assert.Equal(2, _counter.EntryCount);
    }

    [Fact]
    public void BoundaryBurstAllowsDoubleLimitAcrossWindows()
    {
        var limit = MakeLimit(maxRequests: 2, windowSeconds: 60);
        const long windowOneEnd = 1_700_000_039;
        const long windowTwoStart = 1_700_000_040;

        _counter.Attempt(limit, At(windowOneEnd));
        _counter.Attempt(limit, At(windowOneEnd));

        var rejectedEndOfWindow = _counter.Attempt(limit, At(windowOneEnd));
        Assert.False(rejectedEndOfWindow.Allowed);

        var allowedNewWindow = _counter.Attempt(limit, At(windowTwoStart));
        Assert.True(allowedNewWindow.Allowed);
        Assert.Equal(1, allowedNewWindow.CurrentCount);
    }

    [Fact]
    public void ResetClearsStore()
    {
        var limit = MakeLimit(maxRequests: 100);

        _counter.Attempt(limit, At(1_700_000_000));
        Assert.Equal(1, _counter.EntryCount);

        _counter.Reset();

        Assert.Equal(0, _counter.EntryCount);
    }

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    private static ResolvedLimit MakeLimit(
        string key = "client:org_acme",
        string type = "per_client",
        string name = "standard",
        int maxRequests = 100,
        int windowSeconds = 60) => new(key, type, name, maxRequests, windowSeconds);
}
