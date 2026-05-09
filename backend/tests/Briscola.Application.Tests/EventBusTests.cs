using Briscola.Application.Bus;
using Briscola.Application.Orchestration.Events;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests;

public sealed class EventBusTests
{
    [Fact]
    public async Task In_memory_bus_reads_published_events_in_order()
    {
        InMemoryGameEventBus bus = new();
        Guid gameId = Guid.NewGuid();
        DateTimeOffset at = DateTimeOffset.UtcNow;

        await bus.PublishAsync(new PhaseChangedEvent(gameId, at, GamePhase.Playing));
        await bus.PublishAsync(new PhaseChangedEvent(gameId, at, GamePhase.LastHand));

        await using IAsyncEnumerator<IGameEvent> reader =
            bus.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Should().BeOfType<PhaseChangedEvent>()
            .Which.NewPhase.Should().Be(GamePhase.Playing);
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Should().BeOfType<PhaseChangedEvent>()
            .Which.NewPhase.Should().Be(GamePhase.LastHand);
    }
}
