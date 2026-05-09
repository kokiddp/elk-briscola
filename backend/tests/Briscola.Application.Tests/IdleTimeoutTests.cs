using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Orchestration;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;

namespace Briscola.Application.Tests;

public sealed class IdleTimeoutTests
{
    [Fact]
    public async Task Warns_at_idle_threshold_then_forfeits_at_deadline()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();
        int idleSeat = room.CurrentState.NextToPlaySeat;

        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(5));

        IdleWarningEvent warning = factory.Bus.Events.OfType<IdleWarningEvent>().Single();
        warning.SeatIndex.Should().Be(idleSeat);

        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(5));

        GameFinishedEvent finished = factory.Bus.Events.OfType<GameFinishedEvent>().Single();
        finished.Reason.Should().Be(EndedReason.ForfeitIdle);
    }

    [Fact]
    public async Task Playing_a_card_resets_idle_warning_for_next_turn()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        int seat = room.CurrentState.NextToPlaySeat;
        Domain.Primitives.Card card = room.CurrentState.Hands[seat][0];

        await room.EnqueueAsync(new PlayCardCommand(room.CurrentState.GameId, users[seat], card));
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(5));

        factory.Bus.Events.OfType<IdleWarningEvent>().Single().SeatIndex
            .Should().Be(room.CurrentState.NextToPlaySeat);
    }
}
