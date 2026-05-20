using Briscola.Api.Hubs.Limits;
using Briscola.Application.Ports;
using FluentAssertions;
using Xunit;

namespace Briscola.Api.IntegrationTests.Unit;

/// <summary>
/// In-process tests for the per-user hub rate limiter. The audit's H1
/// finding called out the previous per-connection design: a single
/// user opening N WebSockets multiplied the budget by N. These tests
/// pin the new contract — buckets are keyed by <c>(userId, method)</c>.
/// </summary>
public sealed class HubMethodRateLimiterTests
{
    [Fact]
    public void Admits_calls_up_to_the_permit_limit_then_rejects()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        HubMethodRateLimiter limiter = new(clock);
        Guid user = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire(user, "SendChat", permitLimit: 5, window: TimeSpan.FromSeconds(10))
                .Should().BeTrue($"the {i + 1}-th call should land inside the budget");
        }

        limiter.TryAcquire(user, "SendChat", permitLimit: 5, window: TimeSpan.FromSeconds(10))
            .Should().BeFalse("the 6th call is over the 5-per-10s budget");
    }

    [Fact]
    public void Refills_after_the_window_slides()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        HubMethodRateLimiter limiter = new(clock);
        Guid user = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
        limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10)).Should().BeFalse();

        // Advance past the window — the oldest timestamp falls off.
        clock.Advance(TimeSpan.FromSeconds(11));
        limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10))
            .Should().BeTrue("after the window slides past all five timestamps the budget refills");
    }

    [Fact]
    public void Two_users_have_independent_budgets()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        HubMethodRateLimiter limiter = new(clock);
        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        // Alice exhausts her budget.
        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire(alice, "SendChat", 5, TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
        limiter.TryAcquire(alice, "SendChat", 5, TimeSpan.FromSeconds(10)).Should().BeFalse();

        // Bob still has full budget — buckets are keyed by userId.
        limiter.TryAcquire(bob, "SendChat", 5, TimeSpan.FromSeconds(10))
            .Should().BeTrue("Bob shouldn't be throttled because Alice exhausted hers");
    }

    [Fact]
    public void One_user_cannot_double_budget_by_opening_a_second_connection()
    {
        // This is the H1 regression test. Before the fix, each WebSocket
        // got its own per-connection limiter; here we model two
        // connections under the same user by calling the singleton
        // limiter twice with the same userId — same bucket, no doubling.
        FakeClock clock = new(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        HubMethodRateLimiter limiter = new(clock);
        Guid user = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10)).Should().BeTrue();
        }

        // Simulate the second connection trying to spend more permits.
        limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10))
            .Should().BeFalse("opening a second connection must not multiply the budget");
    }

    [Fact]
    public void Different_methods_have_independent_buckets()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
        HubMethodRateLimiter limiter = new(clock);
        Guid user = Guid.NewGuid();

        // PlayCard is 1/sec — fill it.
        limiter.TryAcquire(user, "PlayCard", 1, TimeSpan.FromSeconds(1)).Should().BeTrue();
        limiter.TryAcquire(user, "PlayCard", 1, TimeSpan.FromSeconds(1)).Should().BeFalse();

        // SendChat budget is untouched.
        limiter.TryAcquire(user, "SendChat", 5, TimeSpan.FromSeconds(10))
            .Should().BeTrue("PlayCard's bucket is independent of SendChat's");
    }

    private sealed class FakeClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
