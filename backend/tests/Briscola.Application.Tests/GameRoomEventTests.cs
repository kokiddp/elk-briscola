using System.Collections.Immutable;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Application.Tests;

public sealed class GameRoomEventTests
{
    [Fact]
    public async Task Completing_trick_emits_resolution_and_draw_events()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = room.CurrentState.GameId;

        int firstSeat = room.CurrentState.NextToPlaySeat;
        Card firstCard = room.CurrentState.Hands[firstSeat][0];
        await room.EnqueueAsync(new PlayCardCommand(gameId, users[firstSeat], firstCard));

        int secondSeat = room.CurrentState.NextToPlaySeat;
        Card secondCard = room.CurrentState.Hands[secondSeat][0];
        await room.EnqueueAsync(new PlayCardCommand(gameId, users[secondSeat], secondCard));

        factory.Bus.Events.OfType<TrickResolvedEvent>().Should().ContainSingle();
        factory.Bus.Events.OfType<CardsDrawnEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task View_own_pile_is_rejected_before_last_hand()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();

        await room.EnqueueAsync(new ViewOwnPileCommand(room.CurrentState.GameId, users[0]));

        factory.Bus.Events.OfType<InvalidMoveRejectedEvent>().Should().ContainSingle()
            .Which.Code.Should().Be(InvalidMoveCode.PileViewNotAllowed);
    }

    [Fact]
    public async Task Unknown_user_play_is_rejected()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();
        int seat = room.CurrentState.NextToPlaySeat;
        Card card = room.CurrentState.Hands[seat][0];

        await room.EnqueueAsync(new PlayCardCommand(room.CurrentState.GameId, Guid.NewGuid(), card));

        factory.Bus.Events.OfType<InvalidMoveRejectedEvent>().Should().ContainSingle()
            .Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }

    [Fact]
    public async Task Unknown_user_disconnect_and_reconnect_are_ignored()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();
        Guid unknown = Guid.NewGuid();

        await room.EnqueueAsync(new DisconnectCommand(room.CurrentState.GameId, unknown));
        await room.EnqueueAsync(new ReconnectCommand(room.CurrentState.GameId, unknown));

        factory.Bus.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Unsupported_command_sent_to_room_fails()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();

        // Use an ad-hoc subclass of GameCommand to exercise the room's
        // default branch. The base record is publicly extensible so a future
        // command type that's missing from the dispatch table fails loudly
        // rather than being silently dropped.
        Func<Task> act = () => room.EnqueueAsync(new UnknownCommand(room.CurrentState.GameId));

        await act.Should().ThrowAsync<GameCommandException>();
    }

    private sealed record UnknownCommand(Guid GameId) : GameCommand(GameId);

    [Fact]
    public void From_record_requires_snapshot()
    {
        TestGameFactory factory = new();
        GameRecord record = new(
            Guid.NewGuid(),
            GameMode.TwoPlayer,
            "bad",
            GameStatus.Running,
            Guid.NewGuid(),
            factory.Clock.UtcNow,
            factory.Clock.UtcNow,
            EndedAt: null,
            ShuffleSeed: 0,
            StateSnapshotJson: string.Empty,
            BriscolaSuit: Suit.Bastoni,
            IsPrivate: false,
            PasswordHash: null,
            [Guid.NewGuid(), Guid.NewGuid()],
            Version: 0);

        Action act = () => GameRoom.FromRecord(
            record,
            factory.GamesFactory,
            factory.Codec,
            factory.Engine,
            factory.Bus,
            factory.Clock,
            factory.Timers,
            factory.OptionsWrapper());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Stale_room_snapshot_throws_concurrency_conflict_on_persist()
    {
        TestGameFactory factory = new();
        (GameRoom room, GameRecord record, Guid[] users) = await factory.CreateRunningRoomAsync();
        await factory.Games.UpdateAsync(record with { Name = "external update" }, CancellationToken.None);
        int seat = room.CurrentState.NextToPlaySeat;
        Card card = room.CurrentState.Hands[seat][0];

        Func<Task> act = () => room.EnqueueAsync(new PlayCardCommand(record.Id, users[seat], card));

        await act.Should().ThrowAsync<GameCommandException>();
    }

    [Fact]
    public async Task View_own_pile_in_last_hand_sends_only_target_snapshot()
    {
        TestGameFactory factory = new();
        (GameRoom _, GameRecord record, Guid[] users) = await factory.CreateRunningRoomAsync();
        Domain.State.GameState state = factory.Codec.Deserialize(record.StateSnapshotJson);
        Card captured = state.Hands[0][0];
        Domain.State.GameState lastHand = state with
        {
            Phase = GamePhase.LastHand,
            Pozzi = state.Pozzi.SetItem(0, ImmutableArray.Create(captured)),
        };
        GameRecord lastHandRecord = record with
        {
            StateSnapshotJson = factory.Codec.Serialize(lastHand),
        };
        GameRoom room = GameRoom.FromRecord(
            lastHandRecord,
            factory.GamesFactory,
            factory.Codec,
            factory.Engine,
            factory.Bus,
            factory.Clock,
            factory.Timers,
            factory.OptionsWrapper());

        await room.EnqueueAsync(new ViewOwnPileCommand(room.CurrentState.GameId, users[0]));

        StateUpdatedEvent update = factory.Bus.Events.OfType<StateUpdatedEvent>().Single();
        update.TargetUserId.Should().Be(users[0]);
        update.Snapshot.MyPozzo.Should().NotBeNull();
        update.Snapshot.MyPozzo!.Value.Should().ContainSingle().Which.Should().Be(captured);
    }

    [Fact]
    public async Task Four_player_disconnect_forfeits_disconnected_team()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync(GameMode.FourPlayerTeams);
        Guid gameId = room.CurrentState.GameId;

        await room.EnqueueAsync(new DisconnectCommand(gameId, users[2]));
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(11));

        GameFinishedEvent finished = factory.Bus.Events.OfType<GameFinishedEvent>().Single();
        finished.Outcome.Should().BeEquivalentTo(new Domain.State.GameOutcome.Winner(1));
    }

    [Fact]
    public async Task Completing_final_trick_saves_normal_draw_result()
    {
        TestGameFactory factory = new();
        Guid[] users = [Guid.NewGuid(), Guid.NewGuid()];
        Card first = new(Suit.Bastoni, Rank.Due);
        Card second = new(Suit.Coppe, Rank.Due);
        ImmutableArray<Card> remainingDeck = CardTables.FullDeck
            .Where(card => card != first && card != second)
            .ToImmutableArray();
        ImmutableArray<Card> pileZero = FindPointSubset(remainingDeck, points: 60);
        ImmutableArray<Card> pileOne = remainingDeck.RemoveRange(pileZero);
        GameState state = new()
        {
            GameId = Guid.NewGuid(),
            Mode = GameMode.TwoPlayer,
            ShuffleSeed = 99,
            DealerSeat = 1,
            Hands = [ImmutableArray.Create(first), ImmutableArray.Create(second)],
            Pozzi = [pileZero, pileOne],
            Stock = [],
            BriscolaCard = first,
            BriscolaSuit = Suit.Spade,
            CurrentTrick = [],
            LeaderSeat = 0,
            NextToPlaySeat = 0,
            Phase = GamePhase.LastHand,
            TrickNumber = 19,
            SeatScores =
            [
                pileZero.Sum(static card => CardTables.Points(card.Rank)),
                pileOne.Sum(static card => CardTables.Points(card.Rank)),
            ],
            Outcome = null,
        };
        GameRecord record = new(
            state.GameId,
            GameMode.TwoPlayer,
            "final trick",
            GameStatus.Running,
            users[0],
            factory.Clock.UtcNow,
            factory.Clock.UtcNow,
            EndedAt: null,
            state.ShuffleSeed,
            factory.Codec.Serialize(state),
            state.BriscolaSuit,
            IsPrivate: false,
            PasswordHash: null,
            users.Select(u => (Guid?)u).ToImmutableArray(),
            Version: 0);
        await factory.Games.CreateAsync(record, CancellationToken.None);
        GameRoom room = GameRoom.FromRecord(
            record,
            factory.GamesFactory,
            factory.Codec,
            factory.Engine,
            factory.Bus,
            factory.Clock,
            factory.Timers,
            factory.OptionsWrapper());

        await room.EnqueueAsync(new PlayCardCommand(state.GameId, users[0], first));
        await room.EnqueueAsync(new PlayCardCommand(state.GameId, users[1], second));

        GameFinishedEvent finished = factory.Bus.Events.OfType<GameFinishedEvent>().Single();
        finished.Reason.Should().Be(EndedReason.Normal);
        finished.Outcome.Should().BeOfType<GameOutcome.Draw>();
        factory.Games.Results.Should().ContainSingle().Which.Kind.Should().Be(GameOutcomeKind.Draw);
    }

    [Fact]
    public async Task Forfeit_on_disconnect_command_ignored_when_seat_is_connected()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();

        await room.EnqueueAsync(new ForfeitOnDisconnectCommand(room.CurrentState.GameId, room.CurrentState.NextToPlaySeat));

        factory.Bus.Events.OfType<GameFinishedEvent>().Should().BeEmpty();
        room.CurrentState.Phase.Should().Be(GamePhase.Playing);
    }

    [Fact]
    public async Task Finished_game_rejects_later_play()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, Guid[] users) = await factory.CreateRunningRoomAsync();
        Guid gameId = room.CurrentState.GameId;
        await room.EnqueueAsync(new DisconnectCommand(gameId, users[0]));
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(11));
        int seat = room.CurrentState.NextToPlaySeat;
        Card card = room.CurrentState.Hands[seat][0];

        await room.EnqueueAsync(new PlayCardCommand(gameId, users[seat], card));

        factory.Bus.Events.OfType<InvalidMoveRejectedEvent>().Should().Contain(e =>
            e.Code == InvalidMoveCode.GameFinished);
    }

    [Fact]
    public async Task View_own_pile_by_unknown_user_is_rejected()
    {
        TestGameFactory factory = new();
        (GameRoom room, _, _) = await factory.CreateRunningRoomAsync();

        await room.EnqueueAsync(new ViewOwnPileCommand(room.CurrentState.GameId, Guid.NewGuid()));

        factory.Bus.Events.OfType<InvalidMoveRejectedEvent>().Should().ContainSingle()
            .Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }

    private static ImmutableArray<Card> FindPointSubset(ImmutableArray<Card> cards, int points)
    {
        List<Card> chosen = [];
        bool found = Search(index: 0, remaining: points);
        found.Should().BeTrue();
        return [.. chosen];

        bool Search(int index, int remaining)
        {
            if (remaining == 0)
            {
                return true;
            }

            if (remaining < 0 || index == cards.Length)
            {
                return false;
            }

            Card card = cards[index];
            chosen.Add(card);
            if (Search(index + 1, remaining - CardTables.Points(card.Rank)))
            {
                return true;
            }

            chosen.RemoveAt(chosen.Count - 1);
            return Search(index + 1, remaining);
        }
    }
}
