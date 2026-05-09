using System.Collections.Immutable;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

/// <summary>
/// The engine asserts at runtime that SeatScores.Sum() == 120 when a game
/// transitions to Finished. This test contrives an invalid state — pozzi
/// don't contain the full deck — and verifies that the invariant fires
/// rather than silently producing a corrupt outcome.
/// </summary>
public sealed class ScoreSumInvariantTests
{
    [Fact]
    public void Score_sum_invariant_fires_when_pozzi_are_corrupted()
    {
        // Construct a 2p "almost finished" state:
        //   - Stock empty, briscola already drawn.
        //   - Hands hold exactly 1 card each (so the next trick is the last).
        //   - Pozzi empty (the deliberate corruption — they should hold 38 cards
        //     by this point).
        // Playing out the final trick must trigger the invariant because
        // pozzi will sum to at most 22 points (Asso + Tre = 21).
        var engine = new BriscolaEngine();

        var leaderCard = new Card(Suit.Bastoni, Rank.Asso);   // 11 points
        var followerCard = new Card(Suit.Bastoni, Rank.Tre);  // 10 points

        // Briscola card is some other Bastoni (so leadSuit == briscolaSuit
        // doesn't matter — leader's higher Asso wins anyway).
        var corrupted = new GameState
        {
            GameId = Guid.NewGuid(),
            Mode = GameMode.TwoPlayer,
            ShuffleSeed = 0,
            DealerSeat = 1,
            Hands = ImmutableArray.Create(
                ImmutableArray.Create(leaderCard),
                ImmutableArray.Create(followerCard)),
            Pozzi = ImmutableArray.Create(
                ImmutableArray<Card>.Empty,
                ImmutableArray<Card>.Empty),
            Stock = ImmutableArray<Card>.Empty,
            BriscolaCard = new Card(Suit.Spade, Rank.Re),
            BriscolaSuit = Suit.Spade,
            CurrentTrick = ImmutableArray<PlayedCard>.Empty,
            LeaderSeat = 0,
            NextToPlaySeat = 0,
            Phase = GamePhase.LastHand,
            TrickNumber = 19,
            SeatScores = ImmutableArray.Create(0, 0),
            Outcome = null,
        };

        var afterLead = engine.PlayCard(corrupted, 0, leaderCard);

        Action playOut = () => engine.PlayCard(afterLead, 1, followerCard);

        playOut.Should().Throw<InvalidOperationException>()
            .WithMessage("score-sum invariant violated*");
    }
}
