using Briscola.Application.Orchestration.Timers;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class FakeTimerService(FakeClock clock) : ITimerService
{
    private readonly List<ScheduledTimer> _timers = [];

    public IDisposable ScheduleAt(DateTimeOffset at, Func<CancellationToken, ValueTask> callback)
    {
        ScheduledTimer timer = new(at, callback);
        _timers.Add(timer);
        return timer;
    }

    public async Task AdvanceAsync(TimeSpan by)
    {
        clock.Advance(by);
        List<ScheduledTimer> due = _timers
            .Where(t => !t.Cancelled && t.At <= clock.UtcNow)
            .OrderBy(t => t.At)
            .ToList();

        foreach (ScheduledTimer timer in due)
        {
            timer.Cancelled = true;
            await timer.Callback(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class ScheduledTimer(
        DateTimeOffset at,
        Func<CancellationToken, ValueTask> callback) : IDisposable
    {
        public DateTimeOffset At { get; } = at;
        public Func<CancellationToken, ValueTask> Callback { get; } = callback;
        public bool Cancelled { get; set; }

        public void Dispose() => Cancelled = true;
    }
}
