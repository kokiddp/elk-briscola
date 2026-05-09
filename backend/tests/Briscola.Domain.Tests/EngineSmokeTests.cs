using System.Collections.Immutable;
using Briscola.Domain.Engine;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

/// <summary>
/// Smoke-level coverage of the engine. The exhaustive named-test-class
/// suite lives in Step 1.10. These tests prove the engine end-to-end
/// works at all before we expand coverage.
/// </summary>
public sealed class EngineSmokeTests
{
    private static GameSetup Setup2p(Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        GameMode.TwoPlayer,
        DealerSeat: 0,
        PlayerIds: ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid()));

    private static GameSetup Setup4p() => new(
        Guid.NewGuid(),
        GameMode.FourPlayerTeams,
        DealerSeat: 0,
        PlayerIds: ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    [Fact]
    public void StartGame_2p_initial_state_is_well_formed()
    {
        var engine = new BriscolaEngine();
        var state = engine.StartGame(Setup2p(), new SeededRandomSource(1));

        state.Hands.Should().HaveCount(2);
        state.Hands.Should().OnlyContain(h => h.Length == 3);
        // 40 - 2*3 = 34 cards left in stock (briscola included as the tail).
        state.Stock.Length.Should().Be(34);
        state.Stock[^1].Should().Be(state.BriscolaCard);
        state.BriscolaSuit.Should().Be(state.BriscolaCard.Suit);
        state.LeaderSeat.Should().Be(1, "with dealer at 0, leader is seat 1");
        state.NextToPlaySeat.Should().Be(1);
        state.Phase.Should().Be(GamePhase.Playing);
        state.SeatScores.Should().Equal(0, 0);
        state.Outcome.Should().BeNull();
    }

    [Fact]
    public void StartGame_4p_initial_state_is_well_formed()
    {
        var engine = new BriscolaEngine();
        var state = engine.StartGame(Setup4p(), new SeededRandomSource(7));

        state.Hands.Should().HaveCount(4);
        state.Hands.Should().OnlyContain(h => h.Length == 3);
        // 40 - 4*3 = 28 cards left in stock.
        state.Stock.Length.Should().Be(28);
        state.Stock[^1].Should().Be(state.BriscolaCard);
        state.LeaderSeat.Should().Be(1);
        state.SeatScores.Should().Equal(0, 0, 0, 0);
    }

    [Fact]
    public void PlayCard_rejects_NotYourTurn()
    {
        var engine = new BriscolaEngine();
        var state = engine.StartGame(Setup2p(), new SeededRandomSource(1));

        // Dealer (seat 0) should not be able to lead.
        var theirCard = state.Hands[0][0];
        Action act = () => engine.PlayCard(state, 0, theirCard);

        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.NotYourTurn);
    }

    [Fact]
    public void PlayCard_rejects_CardNotInHand()
    {
        var engine = new BriscolaEngine();
        var state = engine.StartGame(Setup2p(), new SeededRandomSource(1));

        // Try to play a card the player does not actually hold.
        var someoneElsesCard = state.Hands[0][0];
        Action act = () => engine.PlayCard(state, state.NextToPlaySeat, someoneElsesCard);

        act.Should().Throw<InvalidMoveException>().Which.Code.Should().Be(InvalidMoveCode.CardNotInHand);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(99999L)]
    public void Random_legal_play_finishes_with_total_points_120_2p(long seed)
    {
        PlayRandomLegalGameToCompletion(GameMode.TwoPlayer, seed);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(99999L)]
    public void Random_legal_play_finishes_with_total_points_120_4p(long seed)
    {
        PlayRandomLegalGameToCompletion(GameMode.FourPlayerTeams, seed);
    }

    private static void PlayRandomLegalGameToCompletion(GameMode mode, long seed)
    {
        var engine = new BriscolaEngine();
        int n = mode == GameMode.TwoPlayer ? 2 : 4;
        var setup = new GameSetup(
            Guid.NewGuid(),
            mode,
            DealerSeat: 0,
            PlayerIds: ImmutableArray.CreateRange(Enumerable.Range(0, n).Select(_ => Guid.NewGuid())));

        // Use a separate RNG for move selection so the deal seed stays
        // independent of the play strategy.
        var moveRng = new Random(unchecked((int)(seed ^ 0xCAFEBABE)));
        var state = engine.StartGame(setup, new SeededRandomSource(seed));

        int safety = 200; // a 40-card game has at most 40 plays.
        while (state.Phase != GamePhase.Finished)
        {
            if (--safety < 0) throw new InvalidOperationException("did not terminate");
            int seat = state.NextToPlaySeat;
            var hand = state.Hands[seat];
            var card = hand[moveRng.Next(hand.Length)];
            state = engine.PlayCard(state, seat, card);
        }

        state.SeatScores.Sum().Should().Be(CardTables.TotalDeckPoints).And.Be(120);
        state.Hands.Should().OnlyContain(h => h.Length == 0);
        state.Stock.Length.Should().Be(0);
        state.Outcome.Should().NotBeNull();
    }
}
