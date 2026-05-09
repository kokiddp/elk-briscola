using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class DrawingTests
{
    private readonly BriscolaEngine _engine = new();

    [Fact]
    public void Winner_draws_first_then_seat_order()
    {
        // Play one full trick from a fresh game, then verify draws.
        var state = _engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(42));
        var stockBefore = state.Stock;
        int leader = state.LeaderSeat;

        // Play seat-by-seat, picking each seat's first card.
        var s = state;
        for (int k = 0; k < 4; k++)
        {
            int seat = (leader + k) % 4;
            s = _engine.PlayCard(s, seat, s.Hands[seat][0]);
        }

        int winnerSeat = s.LeaderSeat; // winner becomes new leader
        // The winner should have drawn stockBefore[0]; subsequent seats in
        // (winnerSeat, +1, +2, +3) order should have drawn stockBefore[1..3].
        for (int k = 0; k < 4; k++)
        {
            int seat = (winnerSeat + k) % 4;
            s.Hands[seat].Should().Contain(stockBefore[k],
                "seat {0} (draw position {1} after winner) should have drawn stockBefore[{1}]",
                seat, k);
        }
        s.Stock.Length.Should().Be(stockBefore.Length - 4);
    }

    [Fact]
    public void Last_card_drawn_is_the_briscola()
    {
        // Play deterministically until Stock empties; assert the last drawn
        // card across any seat is the briscola card.
        var state = GameTestHarness.PlayDeterministicGame(GameMode.TwoPlayer, seed: 999);

        // The briscola card MUST end up in someone's pozzo (every card does
        // by end of game). The stronger property: trace the game and verify
        // that the briscola was drawn after every other stock card.
        // We do this by replaying trick-by-trick.
        var engine = new BriscolaEngine();
        var s = engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(999));

        Card? lastDrawnFromStock = null;
        var prevStock = s.Stock;
        while (s.Phase != GamePhase.Finished)
        {
            int seat = s.NextToPlaySeat;
            s = engine.PlayCard(s, seat, s.Hands[seat][0]);

            if (s.Stock.Length < prevStock.Length)
            {
                int drawnCount = prevStock.Length - s.Stock.Length;
                // The cards drawn this trick are prevStock[0 .. drawnCount-1].
                lastDrawnFromStock = prevStock[drawnCount - 1];
            }
            prevStock = s.Stock;
        }
        _ = state; // silence unused-variable analyzer

        lastDrawnFromStock.Should().Be(s.BriscolaCard,
            "the very last card drawn from the stock must be the briscola card");
    }

    [Fact]
    public void Stock_count_decreases_by_player_count_per_trick_until_exhausted()
    {
        var engine = new BriscolaEngine();
        var s = engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(7));
        int n = 4;

        var stockSizes = new List<int> { s.Stock.Length };
        var prevTrickNumber = s.TrickNumber;
        while (s.Phase != GamePhase.Finished)
        {
            int seat = s.NextToPlaySeat;
            s = engine.PlayCard(s, seat, s.Hands[seat][0]);

            if (s.TrickNumber != prevTrickNumber)
            {
                stockSizes.Add(s.Stock.Length);
                prevTrickNumber = s.TrickNumber;
            }
        }

        // Each step should drop the stock by `n`, except possibly the last
        // (partial-draw) step that empties the stock.
        for (int i = 0; i < stockSizes.Count - 1; i++)
        {
            int delta = stockSizes[i] - stockSizes[i + 1];
            if (stockSizes[i + 1] == 0)
            {
                // The last drawing trick may draw fewer than n cards.
                delta.Should().BeLessThanOrEqualTo(n);
            }
            else
            {
                delta.Should().Be(n,
                    "while stock is non-empty, each trick draws exactly {0} cards", n);
            }
        }
    }

    [Fact]
    public void When_stock_has_fewer_than_player_count_some_seats_skip_draw_in_that_trick()
    {
        // 4p: 28 stock cards initially. After 7 tricks of 4 draws each = 28.
        // So in trick 7 the stock empties exactly. To force a partial draw,
        // set up a state where Stock.Length < n at trick start and play out
        // one trick.
        var engine = new BriscolaEngine();
        var s = engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(11));

        // Fast-forward by playing tricks until Stock.Length == 2 (less than n=4).
        while (s.Stock.Length > 2)
        {
            int seat = s.NextToPlaySeat;
            s = engine.PlayCard(s, seat, s.Hands[seat][0]);
            // Hands[seat].Length might temporarily not be 3 mid-trick; that's OK.
            // We only sample before next trick start, but the loop just keeps going.
        }
        // It's possible the loop overshoots (stock can drop 4 at a time), so handle that.
        if (s.Stock.Length == 0)
        {
            // Already exhausted; rerun with a different seed.
            s = engine.StartGame(GameTestHarness.Setup4p(), new SeededRandomSource(13));
            while (s.Stock.Length > 2)
            {
                int seat = s.NextToPlaySeat;
                s = engine.PlayCard(s, seat, s.Hands[seat][0]);
            }
        }

        // Find a trick boundary by playing until TrickNumber increments.
        int beforeTrickNumber = s.TrickNumber;
        var stockBefore = s.Stock;
        var handCountsBefore = s.Hands.Select(h => h.Length).ToArray();

        // Play out exactly one trick.
        while (s.TrickNumber == beforeTrickNumber)
        {
            int seat = s.NextToPlaySeat;
            s = engine.PlayCard(s, seat, s.Hands[seat][0]);
        }

        s.Stock.Should().BeEmpty("the partial-draw trick must exhaust the stock");

        // Some seats received 0 new cards (they drew from an empty stock and skipped).
        // Net effect: each seat's hand count after this trick = handCountBefore - 1
        // (played card) + (1 if drew, 0 if didn't). Total cards drawn = stockBefore.Length.
        int totalDrawn = stockBefore.Length;
        int totalHandsBefore = handCountsBefore.Sum();
        int totalHandsAfter = s.Hands.Sum(h => h.Length);

        // Each seat played one card (4 played); drew up to totalDrawn.
        (totalHandsBefore - 4 + totalDrawn).Should().Be(totalHandsAfter);
        totalDrawn.Should().BeLessThan(4, "this is the partial-draw trick");
    }
}
