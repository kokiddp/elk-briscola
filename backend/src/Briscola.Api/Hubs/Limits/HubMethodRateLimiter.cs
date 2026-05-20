using System.Collections.Concurrent;
using Briscola.Application.Ports;

namespace Briscola.Api.Hubs.Limits;

/// <summary>
/// Sliding-window rate limiter scoped to a <c>(userId, method)</c>
/// pair. Registered as a singleton so opening multiple SignalR
/// connections under the same user can't multiply the budget — a per-
/// connection limiter (the previous design) was bypassable by simply
/// spinning up a second WebSocket. ASP.NET Core's HTTP rate limiter
/// doesn't reach hub method calls, so this owns that surface.
///
/// Implementation: a small ring of timestamps per <c>(userId, method)</c>.
/// <see cref="TryAcquire"/> drops timestamps that fell outside the
/// window, then admits if there's room. Buckets are not evicted on
/// disconnect — the per-bucket memory cost is bounded by
/// <c>permitLimit</c> timestamps (~32 bytes each) so even 100k unique
/// users × the 2 throttled methods stays under ~10 MB.
/// </summary>
public sealed class HubMethodRateLimiter
{
    private readonly ConcurrentDictionary<BucketKey, RateBucket> _buckets = new();
    private readonly IClock _clock;

    public HubMethodRateLimiter(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Tries to admit one call from <paramref name="userId"/> against
    /// <paramref name="method"/>. Returns false when the sliding-window
    /// budget is exhausted; the caller is expected to reject the hub
    /// invocation with <c>InvalidMove("RateLimited")</c>.
    /// </summary>
    public bool TryAcquire(Guid userId, string method, int permitLimit, TimeSpan window)
    {
        DateTimeOffset now = _clock.UtcNow;
        BucketKey key = new(userId, method);
        RateBucket bucket = _buckets.GetOrAdd(key, _ => new RateBucket(permitLimit, window));
        return bucket.TryAcquire(now);
    }

    private readonly record struct BucketKey(Guid UserId, string Method);

    private sealed class RateBucket
    {
        private readonly int _permitLimit;
        private readonly TimeSpan _window;
        private readonly Queue<DateTimeOffset> _timestamps;
        private readonly object _gate = new();

        public RateBucket(int permitLimit, TimeSpan window)
        {
            _permitLimit = permitLimit;
            _window = window;
            _timestamps = new Queue<DateTimeOffset>(permitLimit);
        }

        public bool TryAcquire(DateTimeOffset now)
        {
            lock (_gate)
            {
                DateTimeOffset cutoff = now - _window;
                while (_timestamps.Count > 0 && _timestamps.Peek() <= cutoff)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count >= _permitLimit)
                {
                    return false;
                }

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}
