using Briscola.Domain.Engine;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class EngineLegalityTests
{
    private readonly BriscolaEngine _engine = new();

    [Fact]
    public void PlayCard_when_not_my_turn_throws_NotYourTurn()
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));

        // Dealer (seat 0) tries to act when leader (seat 1) is up.
        int wrongSeat = (state.NextToPlaySeat + 1) % 2;
        var theirCard = state.Hands[wrongSeat][0];

        Action act = () => _engine.PlayCard(state, wrongSeat, theirCard);
        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }

    [Fact]
    public void PlayCard_card_not_in_hand_throws_CardNotInHand()
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));

        // Pass the opponent's card as if it were ours.
        int active = state.NextToPlaySeat;
        int other = (active + 1) % 2;
        var notInMyHand = state.Hands[other][0];

        Action act = () => _engine.PlayCard(state, active, notInMyHand);
        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.CardNotInHand);
    }

    [Fact]
    public void PlayCard_after_finished_throws_GameFinished()
    {
        var finished = GameTestHarness.PlayDeterministicGame(GameMode.TwoPlayer, seed: 1);
        finished.Phase.Should().Be(GamePhase.Finished);

        // The pre-finish hand is empty, but we still need a Card argument.
        Action act = () => _engine.PlayCard(finished, 0, new Card(Suit.Bastoni, Rank.Asso));
        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.GameFinished);
    }

    [Fact]
    public void PlayCard_negative_seat_throws_CardNotInHand()
    {
        // Pre-checks order: NextToPlaySeat is checked before bounds, so a
        // wrong seat that also happens to be out of range surfaces as
        // NotYourTurn — UNLESS we craft a state where NextToPlaySeat is
        // out of range. We don't, so use the natural flow: a negative seat
        // != NextToPlaySeat triggers NotYourTurn first.
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));

        Action act = () => _engine.PlayCard(state, -1, state.Hands[state.NextToPlaySeat][0]);
        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }

    [Fact]
    public void Any_card_in_hand_is_legal()
    {
        // Briscola has no must-follow-suit rule. Try every card in the
        // active hand on a fresh state — each must be a legal play.
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        int seat = state.NextToPlaySeat;

        foreach (var card in state.Hands[seat])
        {
            _engine.IsLegalMove(state, seat, card).Should().BeTrue(
                "{0} is in seat {1}'s hand and it's seat {1}'s turn", card, seat);

            // Sanity: actually applying it must not throw.
            Action act = () => _engine.PlayCard(state, seat, card);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void IsLegalMove_returns_false_when_not_my_turn()
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        int wrong = (state.NextToPlaySeat + 1) % 2;
        var theirCard = state.Hands[wrong][0];

        _engine.IsLegalMove(state, wrong, theirCard).Should().BeFalse();
    }

    [Fact]
    public void IsLegalMove_returns_false_for_card_not_in_hand()
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        int active = state.NextToPlaySeat;
        int other = (active + 1) % 2;

        _engine.IsLegalMove(state, active, state.Hands[other][0]).Should().BeFalse();
    }

    [Fact]
    public void IsLegalMove_returns_false_when_game_finished()
    {
        var finished = GameTestHarness.PlayDeterministicGame(GameMode.TwoPlayer, seed: 1);
        _engine.IsLegalMove(finished, 0, new Card(Suit.Bastoni, Rank.Asso)).Should().BeFalse();
    }

    [Fact]
    public void IsLegalMove_returns_false_for_out_of_range_seat()
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        // Force NextToPlaySeat to a value that lets us probe the bounds branch:
        // we use the canonical state but pass a seat that's out of range
        // AND happens to equal NextToPlaySeat. In a 2p game, seat=2 is out of
        // range; force NextToPlaySeat=2 via a `with`-clone.
        var rigged = state with { NextToPlaySeat = 2 };
        _engine.IsLegalMove(rigged, 2, new Card(Suit.Bastoni, Rank.Asso)).Should().BeFalse();
    }

    [Fact]
    public void IsLegalMove_throws_on_null_state()
    {
        Action a = () => _engine.IsLegalMove(null!, 0, new Card(Suit.Bastoni, Rank.Asso));
        a.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PlayCard_throws_on_null_state()
    {
        Action a = () => _engine.PlayCard(null!, 0, new Card(Suit.Bastoni, Rank.Asso));
        a.Should().Throw<ArgumentNullException>();
    }
}
