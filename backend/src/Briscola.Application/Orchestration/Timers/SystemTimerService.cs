using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Ports;

namespace Briscola.Application.Orchestration.Timers;

[ExcludeFromCodeCoverage]
public sealed class SystemTimerService(IClock clock) : ITimerService
{
    public IDisposable ScheduleAt(DateTimeOffset at, Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        TimeSpan due = at - clock.UtcNow;
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        return new TimerRegistration(due, callback);
    }

    private sealed class TimerRegistration : IDisposable
    {
        private readonly Timer _timer;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public TimerRegistration(TimeSpan due, Func<CancellationToken, ValueTask> callback)
        {
            _timer = new Timer(
                static state =>
                {
                    TimerState timerState = (TimerState)state!;
                    _ = timerState.FireAsync();
                },
                new TimerState(callback, _cts.Token),
                due,
                Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
            _timer.Dispose();
            _cts.Dispose();
        }

        private sealed record TimerState(
            Func<CancellationToken, ValueTask> Callback,
            CancellationToken CancellationToken)
        {
            public async Task FireAsync()
            {
                if (CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await Callback(CancellationToken).ConfigureAwait(false);
            }
        }
    }
}
