using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class ScoringTests
{
    public static IEnumerable<object[]> HundredSeeds() =>
        Enumerable.Range(1, 100).Select(i => new object[] { (long)i });

    [Theory]
    [MemberData(nameof(HundredSeeds))]
    public void SeatScores_sum_equals_120_at_finish(long seed)
    {
        var s = GameTestHarness.PlayRandomLegalGame(GameMode.TwoPlayer, seed);

        s.Phase.Should().Be(GamePhase.Finished);
        s.SeatScores.Sum().Should().Be(120);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(99L)]
    public void Team_score_equals_sum_of_seat_scores(long seed)
    {
        var s = GameTestHarness.PlayRandomLegalGame(GameMode.FourPlayerTeams, seed);

        s.Phase.Should().Be(GamePhase.Finished);
        int teamA = s.SeatScores[0] + s.SeatScores[2];
        int teamB = s.SeatScores[1] + s.SeatScores[3];
        (teamA + teamB).Should().Be(120);

        // Outcome must encode the team result, not the seat result.
        if (s.Outcome is GameOutcome.Winner w)
        {
            int expectedTeam = teamA > teamB ? 0 : 1;
            w.SeatOrTeam.Should().Be(expectedTeam);
        }
        else
        {
            s.Outcome.Should().BeOfType<GameOutcome.Draw>();
            teamA.Should().Be(teamB);
        }
    }

    [Fact]
    public void Draw_at_60_60_yields_GameOutcome_Draw()
    {
        // Seed 68 with the random-legal mover yields a 60-60 finish in 2p.
        // The seed selection is documented; if the mover or scoring ever
        // changes, regenerate via the tools/find-draw probe.
        var s = GameTestHarness.PlayRandomLegalGame(GameMode.TwoPlayer, seed: 68);

        s.Phase.Should().Be(GamePhase.Finished);
        s.SeatScores.Should().Equal(60, 60);
        s.Outcome.Should().BeOfType<GameOutcome.Draw>();
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    public void Higher_score_yields_GameOutcome_Winner(long seed)
    {
        var s = GameTestHarness.PlayRandomLegalGame(GameMode.TwoPlayer, seed);

        // Skip if this happens to be a draw; the dedicated draw test covers that.
        if (s.SeatScores[0] == s.SeatScores[1])
        {
            s.Outcome.Should().BeOfType<GameOutcome.Draw>();
            return;
        }

        s.Outcome.Should().BeOfType<GameOutcome.Winner>();
        var winner = (GameOutcome.Winner)s.Outcome!;
        int expected = s.SeatScores[0] > s.SeatScores[1] ? 0 : 1;
        winner.SeatOrTeam.Should().Be(expected);
    }
}
