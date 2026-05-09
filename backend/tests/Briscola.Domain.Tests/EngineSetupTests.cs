using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class EngineSetupTests
{
    private readonly BriscolaEngine _engine = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Deal_2p_gives_3_cards_each(int dealerSeat)
    {
        var state = _engine.StartGame(GameTestHarness.Setup2p(dealerSeat), new SeededRandomSource(1));

        state.Hands.Should().HaveCount(2);
        state.Hands.Should().OnlyContain(h => h.Length == 3);
        state.Hands.SelectMany(h => h).Distinct().Should().HaveCount(6,
            "the 6 dealt cards must all be distinct");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Deal_4p_gives_3_cards_each(int dealerSeat)
    {
        var state = _engine.StartGame(GameTestHarness.Setup4p(dealerSeat), new SeededRandomSource(1));

        state.Hands.Should().HaveCount(4);
        state.Hands.Should().OnlyContain(h => h.Length == 3);
        state.Hands.SelectMany(h => h).Distinct().Should().HaveCount(12);
    }

    [Theory]
    [InlineData(GameMode.TwoPlayer, 1L)]
    [InlineData(GameMode.TwoPlayer, 42L)]
    [InlineData(GameMode.FourPlayerTeams, 1L)]
    [InlineData(GameMode.FourPlayerTeams, 42L)]
    public void Briscola_card_is_at_stock_tail(GameMode mode, long seed)
    {
        var state = _engine.StartGame(GameTestHarness.SetupForMode(mode), new SeededRandomSource(seed));

        state.Stock[^1].Should().Be(state.BriscolaCard);
        state.BriscolaCard.Suit.Should().Be(state.BriscolaSuit);
    }

    [Fact]
    public void Stock_count_initial_2p_equals_33()
    {
        // 40 - (2 players * 3 cards) - 1 briscola card on table = 33 stock cards
        // PLUS the briscola card sitting at the tail of the Stock array = 34 total.
        // The "33" in the spec name refers to the count of *non-briscola* stock
        // cards; our implementation includes the briscola in Stock at the tail.
        var state = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));

        state.Stock.Length.Should().Be(34, "33 regular stock cards + briscola at tail");
        state.Stock.Take(state.Stock.Length - 1).Should().HaveCount(33);
    }

    [Fact]
    public void Stock_count_initial_4p_equals_27()
    {
        // 40 - (4 * 3) - 1 = 27 non-briscola stock cards; total Stock length = 28.
        var state = _engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(1));

        state.Stock.Length.Should().Be(28, "27 regular stock cards + briscola at tail");
        state.Stock.Take(state.Stock.Length - 1).Should().HaveCount(27);
    }

    [Theory]
    [InlineData(GameMode.TwoPlayer, 0)]
    [InlineData(GameMode.TwoPlayer, 1)]
    [InlineData(GameMode.FourPlayerTeams, 0)]
    [InlineData(GameMode.FourPlayerTeams, 1)]
    [InlineData(GameMode.FourPlayerTeams, 2)]
    [InlineData(GameMode.FourPlayerTeams, 3)]
    public void Leader_is_seat_after_dealer_in_seat_order(GameMode mode, int dealerSeat)
    {
        int n = GameTestHarness.PlayerCount(mode);
        var state = _engine.StartGame(GameTestHarness.SetupForMode(mode, dealerSeat), new SeededRandomSource(1));

        int expectedLeader = (dealerSeat + 1) % n;
        state.LeaderSeat.Should().Be(expectedLeader);
        state.NextToPlaySeat.Should().Be(expectedLeader);
        state.DealerSeat.Should().Be(dealerSeat);
    }

    [Theory]
    [InlineData(GameMode.TwoPlayer)]
    [InlineData(GameMode.FourPlayerTeams)]
    public void Leader_hand_equals_deck_indices_0_N_2N(GameMode mode)
    {
        // Round-robin deal order: leader (seat (dealer+1)%n) receives cards
        // at deck indices 0, n, 2n.
        int n = GameTestHarness.PlayerCount(mode);
        const long seed = 12345;

        // Reproduce the deck the engine sees by calling Deck.Shuffled with the same seed.
        var deck = Deck.Shuffled(new SeededRandomSource(seed));
        var expectedLeaderHand = new[] { deck[0], deck[n], deck[2 * n] };

        var state = _engine.StartGame(GameTestHarness.SetupForMode(mode, dealerSeat: 0), new SeededRandomSource(seed));

        state.Hands[state.LeaderSeat].Should().BeEquivalentTo(
            expectedLeaderHand,
            o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void StartGame_throws_for_wrong_player_count()
    {
        var bad = GameTestHarness.Setup2p() with
        {
            PlayerIds = System.Collections.Immutable.ImmutableArray.Create(Guid.NewGuid()),
        };

        Action act = () => _engine.StartGame(bad, new SeededRandomSource(1));
        act.Should().Throw<ArgumentException>().WithMessage("*requires 2 players*");
    }

    [Fact]
    public void StartGame_throws_for_dealer_out_of_range()
    {
        var bad = GameTestHarness.Setup2p() with { DealerSeat = 99 };

        Action act = () => _engine.StartGame(bad, new SeededRandomSource(1));
        act.Should().Throw<ArgumentException>().WithMessage("*out of range*");
    }

    [Fact]
    public void StartGame_throws_on_null_setup()
    {
        Action a = () => _engine.StartGame(null!, new SeededRandomSource(1));
        a.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StartGame_throws_on_null_rng()
    {
        Action a = () => _engine.StartGame(GameTestHarness.Setup2p(), null!);
        a.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Initial_phase_is_Playing()
    {
        var s = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        s.Phase.Should().Be(GamePhase.Playing);
    }

    [Fact]
    public void Initial_pozzi_and_scores_are_empty()
    {
        var s = _engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(1));
        s.Pozzi.Should().HaveCount(4).And.OnlyContain(p => p.Length == 0);
        s.SeatScores.Should().Equal(0, 0, 0, 0);
        s.CurrentTrick.Should().BeEmpty();
        s.TrickNumber.Should().Be(0);
        s.Outcome.Should().BeNull();
    }

    [Fact]
    public void ShuffleSeed_is_persisted_in_state()
    {
        const long seed = 7777;
        var s = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(seed));
        s.ShuffleSeed.Should().Be(seed);
    }
}
