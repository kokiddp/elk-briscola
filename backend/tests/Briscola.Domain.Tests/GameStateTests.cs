using System.Collections.Immutable;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class GameStateTests
{
    [Fact]
    public void Constructed_GameState_round_trips_all_fields()
    {
        var briscola = new Card(Suit.Spade, Rank.Re);
        var state = new GameState
        {
            GameId = Guid.NewGuid(),
            Mode = GameMode.TwoPlayer,
            ShuffleSeed = 12345L,
            DealerSeat = 0,
            Hands = ImmutableArray.Create(
                ImmutableArray<Card>.Empty,
                ImmutableArray<Card>.Empty),
            Pozzi = ImmutableArray.Create(
                ImmutableArray<Card>.Empty,
                ImmutableArray<Card>.Empty),
            Stock = ImmutableArray<Card>.Empty,
            BriscolaCard = briscola,
            BriscolaSuit = briscola.Suit,
            CurrentTrick = ImmutableArray<PlayedCard>.Empty,
            LeaderSeat = 1,
            NextToPlaySeat = 1,
            Phase = GamePhase.Dealing,
            TrickNumber = 0,
            SeatScores = ImmutableArray.Create(0, 0),
            Outcome = null,
        };

        state.Mode.Should().Be(GameMode.TwoPlayer);
        state.BriscolaSuit.Should().Be(Suit.Spade);
        state.SeatScores.Should().HaveCount(2).And.OnlyContain(s => s == 0);
        state.Outcome.Should().BeNull();
    }

    [Fact]
    public void GameOutcome_pattern_matches_winner_and_draw()
    {
        GameOutcome winner = new GameOutcome.Winner(1);
        GameOutcome draw = new GameOutcome.Draw();

        (winner switch
        {
            GameOutcome.Winner w => $"win:{w.SeatOrTeam}",
            GameOutcome.Draw => "draw",
            _ => "?",
        }).Should().Be("win:1");

        (draw switch
        {
            GameOutcome.Winner => "win",
            GameOutcome.Draw => "draw",
            _ => "?",
        }).Should().Be("draw");
    }
}
