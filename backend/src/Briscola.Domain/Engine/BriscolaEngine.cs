using System.Collections.Immutable;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Engine;

public sealed class BriscolaEngine : IBriscolaEngine
{
    public GameState StartGame(GameSetup setup, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(rng);

        int n = PlayerCount(setup.Mode);
        if (setup.PlayerIds.Length != n)
        {
            throw new ArgumentException(
                $"Mode {setup.Mode} requires {n} players, got {setup.PlayerIds.Length}",
                nameof(setup));
        }
        if (setup.DealerSeat < 0 || setup.DealerSeat >= n)
        {
            throw new ArgumentException(
                $"DealerSeat {setup.DealerSeat} is out of range [0,{n})",
                nameof(setup));
        }

        var deck = Deck.Shuffled(rng);

        // Round-robin deal: 3 rounds * n cards = 3n cards out.
        // Round r: deck[r*n + k] -> seat (dealer + 1 + k) % n.
        var hands = new ImmutableArray<Card>.Builder[n];
        for (int s = 0; s < n; s++)
        {
            hands[s] = ImmutableArray.CreateBuilder<Card>(3);
        }
        for (int r = 0; r < 3; r++)
        {
            for (int k = 0; k < n; k++)
            {
                int seat = (setup.DealerSeat + 1 + k) % n;
                hands[seat].Add(deck[r * n + k]);
            }
        }

        // Briscola is the next card after the deal.
        Card briscolaCard = deck[3 * n];

        // Stock: deck[3n+1 .. 39] followed by the briscola card at the tail.
        var stockBuilder = ImmutableArray.CreateBuilder<Card>(40 - 3 * n);
        for (int i = 3 * n + 1; i < deck.Length; i++)
        {
            stockBuilder.Add(deck[i]);
        }
        stockBuilder.Add(briscolaCard);

        int leader = (setup.DealerSeat + 1) % n;

        return new GameState
        {
            GameId = setup.GameId,
            Mode = setup.Mode,
            ShuffleSeed = rng.Seed,
            DealerSeat = setup.DealerSeat,
            Hands = ImmutableArray.CreateRange(hands.Select(b => b.ToImmutable())),
            Pozzi = ImmutableArray.CreateRange(Enumerable.Repeat(ImmutableArray<Card>.Empty, n)),
            Stock = stockBuilder.ToImmutable(),
            BriscolaCard = briscolaCard,
            BriscolaSuit = briscolaCard.Suit,
            CurrentTrick = ImmutableArray<PlayedCard>.Empty,
            LeaderSeat = leader,
            NextToPlaySeat = leader,
            Phase = GamePhase.Playing,
            TrickNumber = 0,
            SeatScores = ImmutableArray.CreateRange(Enumerable.Repeat(0, n)),
            Outcome = null,
        };
    }

    public bool IsLegalMove(GameState state, int seatIndex, Card card)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase == GamePhase.Finished) return false;
        if (seatIndex != state.NextToPlaySeat) return false;
        if (seatIndex < 0 || seatIndex >= state.Hands.Length) return false;
        return state.Hands[seatIndex].Contains(card);
    }

    public GameState PlayCard(GameState state, int seatIndex, Card card)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Phase == GamePhase.Finished)
        {
            throw new InvalidMoveException(InvalidMoveCode.GameFinished);
        }
        if (seatIndex != state.NextToPlaySeat)
        {
            throw new InvalidMoveException(InvalidMoveCode.NotYourTurn);
        }
        if (seatIndex < 0 || seatIndex >= state.Hands.Length
            || !state.Hands[seatIndex].Contains(card))
        {
            throw new InvalidMoveException(InvalidMoveCode.CardNotInHand);
        }

        int n = state.Hands.Length;

        // Apply the play.
        var hand = state.Hands[seatIndex].Without(card);
        var hands = state.Hands.SetItem(seatIndex, hand);
        var trick = state.CurrentTrick.Add(new PlayedCard(seatIndex, card));

        // Trick incomplete: hand off to next seat.
        if (trick.Length < n)
        {
            return state with
            {
                Hands = hands,
                CurrentTrick = trick,
                NextToPlaySeat = (seatIndex + 1) % n,
            };
        }

        // Trick complete: resolve, capture, draw, transition phase.
        return ResolveCompleteTrick(state, hands, trick);
    }

    private static GameState ResolveCompleteTrick(
        GameState state,
        ImmutableArray<ImmutableArray<Card>> handsAfterPlay,
        ImmutableArray<PlayedCard> trick)
    {
        int n = handsAfterPlay.Length;
        Suit leadSuit = trick[0].Card.Suit;
        Suit briscolaSuit = state.BriscolaSuit;

        // Find the trick winner.
        // Briscolas always beat anything else; among the briscolas, highest strength wins.
        // Otherwise, among lead-suit plays, highest strength wins.
        // (Off-suit non-briscolas cannot win.)
        PlayedCard winner = ResolveWinner(trick, leadSuit, briscolaSuit);
        int winnerSeat = winner.SeatIndex;

        // Capture the trick into the winner's pozzo.
        var winnerPile = state.Pozzi[winnerSeat];
        foreach (var p in trick)
        {
            winnerPile = winnerPile.Add(p.Card);
        }
        var pozzi = state.Pozzi.SetItem(winnerSeat, winnerPile);

        // Recompute scores from pozzi (kept pure).
        var seatScores = ImmutableArray.CreateRange(
            pozzi.Select(pile => pile.Sum(c => CardTables.Points(c.Rank))));

        // Draw: winner first, then the others in seat order.
        // Stop early if the stock runs out mid-draw.
        var stock = state.Stock;
        var hands = handsAfterPlay;
        for (int k = 0; k < n; k++)
        {
            if (stock.Length == 0) break;
            int drawer = (winnerSeat + k) % n;
            Card drawn = stock[0];
            stock = stock.RemoveAt(0);
            hands = hands.SetItem(drawer, hands[drawer].With(drawn));
        }

        // Phase transition.
        GamePhase newPhase = ComputeNextPhase(state.Phase, stock.Length, hands);

        // Compute outcome on transition to Finished.
        GameOutcome? outcome = null;
        if (newPhase == GamePhase.Finished)
        {
            outcome = ComputeOutcome(state.Mode, seatScores);

            // Runtime invariant: total points captured must equal 120 at end of game.
            int totalPoints = seatScores.Sum();
            if (totalPoints != CardTables.TotalDeckPoints)
            {
                throw new InvalidOperationException(
                    $"score-sum invariant violated: expected {CardTables.TotalDeckPoints}, got {totalPoints}");
            }
        }

        return state with
        {
            Hands = hands,
            Pozzi = pozzi,
            Stock = stock,
            CurrentTrick = ImmutableArray<PlayedCard>.Empty,
            LeaderSeat = winnerSeat,
            NextToPlaySeat = winnerSeat,
            SeatScores = seatScores,
            Phase = newPhase,
            TrickNumber = state.TrickNumber + 1,
            Outcome = outcome,
        };
    }

    private static PlayedCard ResolveWinner(
        ImmutableArray<PlayedCard> trick,
        Suit leadSuit,
        Suit briscolaSuit)
    {
        PlayedCard? best = null;
        bool inBriscolaContest = false;

        foreach (var p in trick)
        {
            bool isBriscola = p.Card.Suit == briscolaSuit;
            bool isLead = p.Card.Suit == leadSuit;

            if (isBriscola)
            {
                if (!inBriscolaContest)
                {
                    // First briscola seen: it becomes the candidate, displacing
                    // any prior lead-suit candidate.
                    best = p;
                    inBriscolaContest = true;
                }
                else if (CardTables.Strength(p.Card.Rank) > CardTables.Strength(best!.Value.Card.Rank))
                {
                    best = p;
                }
            }
            else if (isLead && !inBriscolaContest)
            {
                if (best is null
                    || CardTables.Strength(p.Card.Rank) > CardTables.Strength(best.Value.Card.Rank))
                {
                    best = p;
                }
            }
            // else: off-suit non-briscola can never win, ignore.
        }

        // The leader's card always satisfies isLead, so `best` is never null.
        return best!.Value;
    }

    private static GamePhase ComputeNextPhase(
        GamePhase current,
        int stockLength,
        ImmutableArray<ImmutableArray<Card>> hands)
    {
        bool allHandsEmpty = hands.All(h => h.Length == 0);
        if (allHandsEmpty)
        {
            return GamePhase.Finished;
        }
        if (stockLength == 0)
        {
            return GamePhase.LastHand;
        }
        return current == GamePhase.LastHand ? GamePhase.LastHand : GamePhase.Playing;
    }

    private static GameOutcome ComputeOutcome(GameMode mode, ImmutableArray<int> seatScores)
    {
        return mode switch
        {
            GameMode.TwoPlayer => CompareTwo(seatScores[0], seatScores[1], winnerKeyA: 0, winnerKeyB: 1),
            GameMode.FourPlayerTeams => CompareTwo(
                seatScores[0] + seatScores[2],
                seatScores[1] + seatScores[3],
                winnerKeyA: 0,
                winnerKeyB: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode"),
        };

        static GameOutcome CompareTwo(int a, int b, int winnerKeyA, int winnerKeyB)
        {
            if (a > b) return new GameOutcome.Winner(winnerKeyA);
            if (b > a) return new GameOutcome.Winner(winnerKeyB);
            return new GameOutcome.Draw();
        }
    }

    private static int PlayerCount(GameMode mode) => mode switch
    {
        GameMode.TwoPlayer => 2,
        GameMode.FourPlayerTeams => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode"),
    };
}
