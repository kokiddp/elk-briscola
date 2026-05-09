using Briscola.Application.Configuration;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Engine;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests;

public sealed class GameOrchestratorTests
{
    [Fact]
    public async Task Hydrate_loads_running_games_and_enqueue_routes_to_room()
    {
        TestGameFactory factory = new();
        (_, Persistence.GameRecord record, Guid[] users) = await factory.CreateRunningRoomAsync();
        RecordingGameEventBus bus = new();
        GameOrchestrator orchestrator = new(
            factory.Games,
            factory.Codec,
            new BriscolaEngine(),
            bus,
            factory.Clock,
            factory.Timers,
            Options.Create(new GameOptions()));

        await orchestrator.HydrateAsync(CancellationToken.None);
        await orchestrator.EnqueueAsync(
            new ReconnectCommand(record.Id, users[0]),
            CancellationToken.None);

        bus.Events.OfType<JoinedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Enqueue_for_missing_game_throws()
    {
        TestGameFactory factory = new();
        GameOrchestrator orchestrator = NewOrchestrator(factory, new RecordingGameEventBus());

        Func<Task> act = () => orchestrator.EnqueueAsync(
            new ReconnectCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<GameCommandException>();
    }

    [Fact]
    public void Concurrency_conflict_exception_preserves_message()
    {
        Briscola.Application.Errors.ConcurrencyConflictException ex = new("conflict");

        ex.Message.Should().Be("conflict");
    }

    [Fact]
    public async Task Get_or_create_lazy_factory_runs_once()
    {
        TestGameFactory factory = new();
        (_, GameRecord record, _) = await factory.CreateRunningRoomAsync();
        GameOrchestrator orchestrator = NewOrchestrator(factory, new RecordingGameEventBus());
        int calls = 0;

        GameRoom first = orchestrator.GetOrCreate(record.Id, () =>
        {
            calls++;
            return record;
        });
        GameRoom second = orchestrator.GetOrCreate(record.Id, () =>
        {
            calls++;
            return record;
        });

        first.Should().BeSameAs(second);
        calls.Should().Be(1);
    }

    private static GameOrchestrator NewOrchestrator(
        TestGameFactory factory,
        RecordingGameEventBus bus) =>
        new(
            factory.Games,
            factory.Codec,
            new BriscolaEngine(),
            bus,
            factory.Clock,
            factory.Timers,
            Options.Create(new GameOptions()));
}
