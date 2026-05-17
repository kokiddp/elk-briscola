using Briscola.Application.Configuration;
using Briscola.Application.Orchestration;
using Briscola.Application.Telemetry;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Engine;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests;

/// <summary>
/// Phase 11 review fix: <c>GameOrchestrator</c> used to retain Finished
/// games in its in-memory dict until process shutdown. The fix exposes
/// <see cref="GameOrchestrator.DisposeRoomAsync"/> (called from the
/// SignalR dispatcher on <c>GameFinishedEvent</c>) so memory doesn't
/// grow linearly with games-played and <c>briscola.active_games</c>
/// reflects actually-running games.
/// </summary>
public sealed class OrchestratorRoomCleanupTests
{
    [Fact]
    public async Task DisposeRoomAsync_removes_room_and_decrements_metric()
    {
        TestGameFactory factory = new();
        using BriscolaMetrics metrics = new();
        GameOrchestrator orch = new(
            factory.GamesFactory, factory.Codec, new BriscolaEngine(),
            factory.Bus, factory.Clock, factory.Timers,
            Options.Create(new GameOptions()),
            metrics);

        (_, var record, _) = await factory.CreateRunningRoomAsync();
        await orch.HydrateAsync(CancellationToken.None);

        orch.TryGetRoom(record.Id, out var room).Should().BeTrue();
        room.Should().NotBeNull();

        await orch.DisposeRoomAsync(record.Id);

        orch.TryGetRoom(record.Id, out _).Should().BeFalse(
            "DisposeRoomAsync should remove the room from the active set");
    }

    [Fact]
    public async Task DisposeRoomAsync_is_idempotent_for_unknown_or_already_disposed_ids()
    {
        TestGameFactory factory = new();
        using BriscolaMetrics metrics = new();
        GameOrchestrator orch = new(
            factory.GamesFactory, factory.Codec, new BriscolaEngine(),
            factory.Bus, factory.Clock, factory.Timers,
            Options.Create(new GameOptions()),
            metrics);

        Func<Task> act = async () =>
        {
            // Unknown id — no-op.
            await orch.DisposeRoomAsync(Guid.NewGuid());
            // Same id twice — second call no-ops.
            (_, var record, _) = await factory.CreateRunningRoomAsync();
            await orch.HydrateAsync(CancellationToken.None);
            await orch.DisposeRoomAsync(record.Id);
            await orch.DisposeRoomAsync(record.Id);
        };

        await act.Should().NotThrowAsync();
    }
}
