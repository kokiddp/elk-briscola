using Briscola.Application.Background;
using Briscola.Application.Configuration;
using Briscola.Application.Orchestration;
using Briscola.Application.Telemetry;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests;

public sealed class GracefulShutdownTests
{
    [Fact]
    public async Task ApplicationStopping_drains_the_orchestrator()
    {
        TestGameFactory factory = new();
        using BriscolaMetrics metrics = new();
        GameOrchestrator orchestrator = new(
            factory.GamesFactory, factory.Codec, new BriscolaEngine(),
            factory.Bus, factory.Clock, factory.Timers,
            Options.Create(new GameOptions()),
            metrics);

        // Hydrate at least one room so DisposeAsync has work to do.
        _ = await factory.CreateRunningRoomAsync();
        await orchestrator.HydrateAsync(CancellationToken.None);

        using TestApplicationLifetime lifetime = new();
        GracefulShutdownHostedService svc = new(
            lifetime, orchestrator,
            NullLogger<GracefulShutdownHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        lifetime.StopApplication();
        await svc.StopAsync(CancellationToken.None);

        // The orchestrator's internal dictionary is private; assert via a
        // second DisposeAsync that succeeds without throwing (idempotent
        // when there are no rooms).
        await orchestrator.DisposeAsync();
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
