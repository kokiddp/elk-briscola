using System.Diagnostics.Metrics;
using Briscola.Application.Configuration;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Telemetry;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests;

public sealed class BriscolaMetricsTests
{
    [Fact]
    public void Custom_meter_exposes_three_instruments_under_Briscola()
    {
        // Snapshot the instruments published by a fresh metrics instance —
        // guards against accidental rename/removal of names a future
        // Grafana dashboard pins. Uses a set: under parallel test execution
        // we may observe extra publications from sibling tests' Meter
        // instances; we only care that our three names are present.
        HashSet<string> seen = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == BriscolaMetrics.MeterName)
            {
                lock (seen)
                {
                    seen.Add(instrument.Name);
                }
            }
        };
        listener.Start();

        using BriscolaMetrics _ = new();

        lock (seen)
        {
            seen.Should().Contain("briscola.active_games");
            seen.Should().Contain("briscola.connected_players");
            seen.Should().Contain("briscola.moves_total");
        }
    }

    [Fact]
    public async Task ActiveGames_increments_when_orchestrator_loads_a_game()
    {
        TestGameFactory factory = new();
        using BriscolaMetrics metrics = new();

        // Pin the listener to OUR metrics instance's specific instrument.
        // The MeterListener is process-wide: any sibling test that builds
        // a fresh BriscolaMetrics (and emits Add(1)) would otherwise poison
        // the counter we're inspecting. We capture the Instrument reference
        // in InstrumentPublished and compare by identity in the measurement
        // callback so cross-test bleed-through is zero.
        Instrument? ourActiveGames = null;
        int active = 0;
        using MeterListener listener = new();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == BriscolaMetrics.MeterName &&
                inst.Name == "briscola.active_games" &&
                ReferenceEquals(inst, metrics.ActiveGames))
            {
                ourActiveGames = inst;
                l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<int>((inst, m, _, _) =>
        {
            if (ReferenceEquals(inst, ourActiveGames))
            {
                Interlocked.Add(ref active, m);
            }
        });
        listener.Start();

        GameOrchestrator orch = new(
            factory.GamesFactory, factory.Codec, new BriscolaEngine(),
            factory.Bus, factory.Clock, factory.Timers,
            Options.Create(new GameOptions()),
            metrics);

        // Seed a Running game and hydrate.
        _ = await factory.CreateRunningRoomAsync();
        await orch.HydrateAsync(CancellationToken.None);
        active.Should().Be(1);

        await orch.DisposeAsync();
        active.Should().Be(0);
    }

    [Fact]
    public async Task PlayCard_command_publishes_a_CardPlayedEvent()
    {
        // Sanity check: the moves_total metric is incremented in the API
        // dispatcher when a CardPlayedEvent reaches it. From the
        // application boundary we assert the event publication itself
        // happens — the dispatcher↔meter wiring is exercised by the
        // GameEventDispatcher integration tests.
        TestGameFactory factory = new();
        (var room, _, Guid[] users) = await factory.CreateRunningRoomAsync();

        int seat = room.CurrentState.NextToPlaySeat;
        Card card = room.CurrentState.Hands[seat][0];
        await room.EnqueueAsync(new PlayCardCommand(room.CurrentState.GameId, users[seat], card));

        factory.Bus.Events.OfType<CardPlayedEvent>().Should().NotBeEmpty();
    }
}
