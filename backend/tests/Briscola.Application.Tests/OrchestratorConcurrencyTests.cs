using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Orchestration;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Errors;

namespace Briscola.Application.Tests;

public sealed class OrchestratorConcurrencyTests
{
    [Fact]
    public async Task Thousand_interleaved_commands_keep_room_state_consistent()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = room.CurrentState.GameId;

        Task[] producers = Enumerable.Range(0, 8)
            .Select(producer => Task.Run(async () =>
            {
                for (int i = 0; i < 125; i++)
                {
                    Guid user = users[(producer + i) % users.Length];
                    await room.EnqueueAsync(new ReconnectCommand(gameId, user));
                }
            }))
            .ToArray();

        await Task.WhenAll(producers);

        room.CurrentState.SeatScores.Sum().Should().Be(0);
        room.CurrentState.Hands.Should().OnlyContain(static h => h.Length == 3);
        factory.Bus.Events.OfType<JoinedEvent>().Should().HaveCount(1000);
    }

    [Fact]
    public async Task Single_writer_rejects_second_play_from_same_seat_in_same_trick()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        int seat = room.CurrentState.NextToPlaySeat;
        Domain.Primitives.Card card = room.CurrentState.Hands[seat][0];
        PlayCardCommand command = new(room.CurrentState.GameId, users[seat], card);

        await Task.WhenAll(room.EnqueueAsync(command), room.EnqueueAsync(command));

        factory.Bus.Events.OfType<CardPlayedEvent>().Should().ContainSingle();
        factory.Bus.Events.OfType<InvalidMoveRejectedEvent>().Should().ContainSingle()
            .Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }
}
