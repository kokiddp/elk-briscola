using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class DeterminismTests
{
    [Theory]
    [InlineData(GameMode.TwoPlayer, 1L)]
    [InlineData(GameMode.TwoPlayer, 12345L)]
    [InlineData(GameMode.FourPlayerTeams, 1L)]
    [InlineData(GameMode.FourPlayerTeams, 12345L)]
    public void Same_seed_same_random_choices_yields_same_state(GameMode mode, long seed)
    {
        // Two separate runs with identical deal seed AND identical move-picker
        // RNG must produce identical final states (per-seat hands always end
        // up empty; the comparison points are score, outcome, pozzi).
        var a = GameTestHarness.PlayRandomLegalGame(mode, seed);
        var b = GameTestHarness.PlayRandomLegalGame(mode, seed);

        a.SeatScores.Should().Equal(b.SeatScores);
        a.Outcome.Should().Be(b.Outcome);
        a.TrickNumber.Should().Be(b.TrickNumber);
        a.LeaderSeat.Should().Be(b.LeaderSeat);

        // Pozzi must match per-seat (capture order may differ across alternative
        // implementations but with the same sequence of plays it's identical).
        for (int i = 0; i < a.Pozzi.Length; i++)
        {
            a.Pozzi[i].Should().Equal(b.Pozzi[i], "seat {0}'s pozzo must match", i);
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    public void Same_seed_yields_same_initial_state(long seed)
    {
        var engine = new BriscolaEngine();
        var setup = GameTestHarness.Setup2p();
        var a = engine.StartGame(setup, new SeededRandomSource(seed));
        var b = engine.StartGame(setup, new SeededRandomSource(seed));

        a.BriscolaCard.Should().Be(b.BriscolaCard);
        a.Stock.Should().Equal(b.Stock);
        for (int i = 0; i < a.Hands.Length; i++)
        {
            a.Hands[i].Should().Equal(b.Hands[i]);
        }
    }
}
