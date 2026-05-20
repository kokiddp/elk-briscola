using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Briscola.Application.Orchestration.Timers;

[ExcludeFromCodeCoverage]
public sealed partial class SystemTimerService(IClock clock, ILogger<SystemTimerService>? logger = null)
    : ITimerService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "SystemTimerService callback threw — the timer fire was lost.")]
    private static partial void LogCallbackFailed(ILogger logger, Exception ex);

    public IDisposable ScheduleAt(DateTimeOffset at, Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        TimeSpan due = at - clock.UtcNow;
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        return new TimerRegistration(due, callback, logger);
    }

    private sealed class TimerRegistration : IDisposable
    {
        private readonly Timer _timer;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public TimerRegistration(
            TimeSpan due,
            Func<CancellationToken, ValueTask> callback,
            ILogger<SystemTimerService>? logger)
        {
            _timer = new Timer(
                static state =>
                {
                    TimerState timerState = (TimerState)state!;
                    _ = timerState.FireAsync();
                },
                new TimerState(callback, logger, _cts.Token),
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
            ILogger<SystemTimerService>? Logger,
            CancellationToken CancellationToken)
        {
            public async Task FireAsync()
            {
                if (CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await Callback(CancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Without this catch the timer-pool thread silently
                    // dropped any exception thrown by the callback (e.g. a
                    // disposed orchestrator scope), leaving an idle-tick
                    // forfeit unfired with no operator-visible signal.
                    // Audit L8.
                    if (Logger is not null)
                    {
                        LogCallbackFailed(Logger, ex);
                    }
                }
            }
        }
    }
}
