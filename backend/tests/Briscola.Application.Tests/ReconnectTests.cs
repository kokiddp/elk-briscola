using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Orchestration;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;

namespace Briscola.Application.Tests;

public sealed class ReconnectTests
{
    [Fact]
    public async Task Disconnect_then_reconnect_inside_grace_resumes_state()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();

        await room.EnqueueAsync(new DisconnectCommand(room.CurrentState.GameId, users[0]));
        await room.EnqueueAsync(new ReconnectCommand(room.CurrentState.GameId, users[0]));

        factory.Bus.Events.Should().ContainSingle(e => e is PlayerDisconnectedEvent);
        factory.Bus.Events.Should().ContainSingle(e => e is PlayerReconnectedEvent);
        factory.Bus.Events.OfType<JoinedEvent>().Should().ContainSingle();
        room.CurrentState.Phase.Should().Be(Domain.Primitives.GamePhase.Playing);
    }

    [Fact]
    public async Task Reconnect_is_idempotent_after_first_resume()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = room.CurrentState.GameId;

        await room.EnqueueAsync(new DisconnectCommand(gameId, users[0]));
        for (int i = 0; i < 5; i++)
        {
            await room.EnqueueAsync(new ReconnectCommand(gameId, users[0]));
        }

        factory.Bus.Events.OfType<PlayerReconnectedEvent>().Should().ContainSingle();
        factory.Bus.Events.OfType<JoinedEvent>().Should().HaveCount(5);
    }

    [Fact]
    public async Task Disconnect_deadline_forfeits_to_opponent()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = room.CurrentState.GameId;

        await room.EnqueueAsync(new DisconnectCommand(gameId, users[0]));
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(11));

        GameFinishedEvent finished = factory.Bus.Events.OfType<GameFinishedEvent>().Single();
        finished.Reason.Should().Be(EndedReason.ForfeitDisconnect);
        finished.Outcome.Should().BeEquivalentTo(new Domain.State.GameOutcome.Winner(1));
    }
}
