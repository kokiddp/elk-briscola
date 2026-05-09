using Briscola.Application.Ports;
using Microsoft.AspNetCore.SignalR;

namespace Briscola.Api.Hubs.Limits;

/// <summary>
/// Sliding-window rate limiter scoped to a single SignalR
/// <see cref="HubCallerContext"/>. ASP.NET Core's
/// <c>RateLimiter</c> middleware is HTTP-only; hub method calls
/// happen over a long-lived connection where per-IP / per-route
/// bucketing doesn't help. This per-connection limiter is enough
/// for the cases the spec calls out (PlayCard, SendChat) and fits
/// in the connection's <c>Items</c> bag (auto-disposed by SignalR
/// on disconnect).
///
/// Implementation is a small ring of timestamps per (connection,
/// method) pair. <see cref="TryAcquire"/> drops timestamps that
/// fell outside the window, then admits if there's room. The
/// limiter is fenced behind <see cref="GameHub.GetRateLimiter"/>
/// so the hub method handlers stay terse:
///
/// <code>
/// if (!_limiter.TryAcquire(method, now)) {
///     await Clients.Caller.InvalidMove("RateLimited");
///     return;
/// }
/// </code>
/// </summary>
public sealed class HubMethodRateLimiter
{
    private readonly Dictionary<string, RateBucket> _buckets = [];
    private readonly object _gate = new();
    private readonly IClock _clock;

    public HubMethodRateLimiter(IClock clock)
    {
        _clock = clock;
    }

    public bool TryAcquire(string method, int permitLimit, TimeSpan window)
    {
        DateTimeOffset now = _clock.UtcNow;
        lock (_gate)
        {
            if (!_buckets.TryGetValue(method, out RateBucket? bucket))
            {
                bucket = new RateBucket(permitLimit, window);
                _buckets[method] = bucket;
            }

            return bucket.TryAcquire(now);
        }
    }

    private sealed class RateBucket
    {
        private readonly int _permitLimit;
        private readonly TimeSpan _window;
        private readonly Queue<DateTimeOffset> _timestamps;

        public RateBucket(int permitLimit, TimeSpan window)
        {
            _permitLimit = permitLimit;
            _window = window;
            _timestamps = new Queue<DateTimeOffset>(permitLimit);
        }

        public bool TryAcquire(DateTimeOffset now)
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
