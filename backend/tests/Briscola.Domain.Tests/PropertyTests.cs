using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

/// <summary>
/// Property-style tests: 1000 random seeds, every game must terminate in
/// a valid Finished state. Catches regressions across the engine that
/// individual scenarios may miss.
/// </summary>
public sealed class PropertyTests
{
    public static IEnumerable<object[]> ThousandSeeds() =>
        Enumerable.Range(1, 1000).Select(i => new object[] { (long)i });

    [Theory]
    [MemberData(nameof(ThousandSeeds))]
    public void Random_legal_game_2p_terminates_in_valid_finished_state(long seed)
    {
        AssertValidFinishedState(GameMode.TwoPlayer, seed);
    }

    [Theory]
    [MemberData(nameof(ThousandSeeds))]
    public void Random_legal_game_4p_terminates_in_valid_finished_state(long seed)
    {
        AssertValidFinishedState(GameMode.FourPlayerTeams, seed);
    }

    private static void AssertValidFinishedState(GameMode mode, long seed)
    {
        var s = GameTestHarness.PlayRandomLegalGame(mode, seed);

        s.Phase.Should().Be(GamePhase.Finished);
        s.SeatScores.Sum().Should().Be(120);
        s.Outcome.Should().NotBeNull();
        s.Hands.Should().OnlyContain(h => h.Length == 0);
        s.Stock.Should().BeEmpty();

        // All 40 cards live in the pozzi.
        s.Pozzi.SelectMany(p => p).Distinct().Should().HaveCount(40);
    }
}
