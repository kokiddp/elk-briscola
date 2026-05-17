using Briscola.Application.Orchestration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Briscola.Application.Background;

/// <summary>
/// On <see cref="IHostApplicationLifetime.ApplicationStopping"/>:
///
/// 1. Completes every <see cref="GameRoom"/>'s command channel and awaits
///    the per-room process loop (the loop drains queued commands and
///    snapshots state on every accepted move, so the final write lands
///    before the loop exits).
/// 2. Caps the drain at <see cref="DrainBudget"/> seconds so a stuck room
///    never blocks pod-termination beyond the orchestrator's SLO.
///
/// Note: the SignalR hubs deregister themselves naturally when the host
/// stops the underlying server, so we don't need an explicit "refuse new
/// connections" gate — the listener stops accepting after
/// ApplicationStopping fires.
/// </summary>
public sealed partial class GracefulShutdownHostedService : IHostedService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Graceful shutdown drained the orchestrator in {ElapsedMs} ms")]
    private partial void LogDrained(int elapsedMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Graceful shutdown drain budget expired after {ElapsedMs} ms; some rooms may have unflushed state")]
    private partial void LogDrainTimedOut(int elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Graceful shutdown drain failed")]
    private partial void LogDrainFailed(Exception ex);

    public static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(10);

    private readonly IHostApplicationLifetime _lifetime;
    private readonly GameOrchestrator _orchestrator;
    private readonly ILogger<GracefulShutdownHostedService> _logger;
    private CancellationTokenRegistration _registration;

    public GracefulShutdownHostedService(
        IHostApplicationLifetime lifetime,
        GameOrchestrator orchestrator,
        ILogger<GracefulShutdownHostedService> logger)
    {
        _lifetime = lifetime;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registration = _lifetime.ApplicationStopping.Register(OnStopping);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registration.Dispose();
        return Task.CompletedTask;
    }

    private void OnStopping()
    {
        // Wait synchronously: ApplicationStopping callbacks are
        // synchronous and the host won't move to Stopped until we return,
        // but we bound this on DrainBudget so a stuck room can't hold
        // the process open indefinitely.
        long startedAt = Environment.TickCount64;
        try
        {
            using CancellationTokenSource cts = new(DrainBudget);
            // DisposeAsync iterates _rooms, completes each command channel
            // and awaits the process loop — full drain.
            ValueTask drain = _orchestrator.DisposeAsync();
            if (drain.IsCompletedSuccessfully)
            {
                LogDrained(0);
                return;
            }
            drain.AsTask().Wait(cts.Token);
            LogDrained((int)(Environment.TickCount64 - startedAt));
        }
        catch (OperationCanceledException)
        {
            LogDrainTimedOut((int)(Environment.TickCount64 - startedAt));
        }
        catch (Exception ex)
        {
            LogDrainFailed(ex);
        }
    }

}
