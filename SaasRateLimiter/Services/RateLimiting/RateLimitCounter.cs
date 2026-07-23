using System.Collections.Concurrent;

namespace SaasRateLimiter.Services.RateLimiting;

/// <summary>
/// In-memory fixed-window counter, registered as a singleton so state is shared
/// across requests for the lifetime of the process. Kestrel serves requests on a
/// thread pool (unlike PHP's one-request-per-process model), so window rollover
/// and increment are guarded by a per-key lock to keep counts accurate under
/// concurrent hits to the same key.
/// </summary>
public sealed class RateLimitCounter
{
    private sealed class WindowEntry
    {
        public long WindowStart;
        public int Count;
    }

    private readonly ConcurrentDictionary<string, WindowEntry> _store = new();

    public RateLimitResult Attempt(ResolvedLimit limit, DateTimeOffset? now = null)
    {
        var timestamp = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var windowStart = WindowStart(timestamp, limit.WindowSeconds);
        var entry = _store.GetOrAdd(limit.Key, _ => new WindowEntry());

        int count;
        lock (entry)
        {
            if (entry.WindowStart != windowStart)
            {
                entry.WindowStart = windowStart;
                entry.Count = 0;
            }

            count = ++entry.Count;
        }

        var retryAfterSeconds = (int)Math.Max(1, windowStart + limit.WindowSeconds - timestamp);
        var allowed = count <= limit.MaxRequests;

        return new RateLimitResult(allowed, limit.Key, limit.Type, limit.Name, limit.MaxRequests, count, retryAfterSeconds);
    }

    public void Reset() => _store.Clear();

    public int EntryCount => _store.Count;

    private static long WindowStart(long timestamp, int windowSeconds) => timestamp / windowSeconds * windowSeconds;
}
