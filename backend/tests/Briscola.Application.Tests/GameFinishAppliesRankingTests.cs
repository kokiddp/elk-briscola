using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;

namespace Briscola.Application.Tests;

/// <summary>
/// Regression: every time a game finishes, <c>GameRoom.SaveFinishedAsync</c>
/// must call <c>RankingService.ApplyResultAsync</c> alongside the
/// <c>SaveResultAsync</c> persist. Without this wiring the Elo column
/// silently never moves after a match — the bug reported during the
/// Phase 10 review.
/// </summary>
public sealed class GameFinishAppliesRankingTests
{
    [Fact]
    public async Task Idle_forfeit_applies_ranking_to_both_players()
    {
        TestGameFactory factory = new();
        (_, _, Guid[] users) = await factory.CreateRunningRoomAsync();

        // Pre-flight: rankings start at the default Elo seed.
        RankingRecord seatZeroBefore = await factory.Rankings.GetAsync(users[0], CancellationToken.None);
        RankingRecord seatOneBefore = await factory.Rankings.GetAsync(users[1], CancellationToken.None);
        seatZeroBefore.Elo.Should().Be(1500);
        seatOneBefore.Elo.Should().Be(1500);

        // Drive an idle forfeit so the room finishes deterministically.
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(5));
        await factory.Timers.AdvanceAsync(TimeSpan.FromSeconds(5));
        factory.Bus.Events.OfType<GameFinishedEvent>().Should().ContainSingle();

        RankingRecord seatZeroAfter = await factory.Rankings.GetAsync(users[0], CancellationToken.None);
        RankingRecord seatOneAfter = await factory.Rankings.GetAsync(users[1], CancellationToken.None);

        // Both rankings moved by exactly one game; total games played bumped.
        seatZeroAfter.GamesPlayed.Should().Be(1);
        seatOneAfter.GamesPlayed.Should().Be(1);
        // Elo of one moved up, the other moved down by the same magnitude
        // (zero-sum at equal seeds). Whoever forfeited loses.
        int delta = Math.Abs(seatZeroAfter.Elo - 1500);
        delta.Should().BeGreaterThan(0);
        Math.Abs(seatOneAfter.Elo - 1500).Should().Be(delta);
        (seatZeroAfter.Elo + seatOneAfter.Elo).Should().Be(3000);
    }
}
