using System.Collections.Immutable;
using Briscola.Application.Persistence;
using Briscola.Application.Ranking;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests;

public sealed class RankingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Equal_ratings_win_moves_twelve_points()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        InMemoryRankingRepository repo = Seed(a, 1500, b, 1500);
        RankingService service = new(repo, new FakeClock(Now));

        await service.ApplyResultAsync(
            Game(a, b),
            Result(GameOutcomeKind.Win, winnerKey: 0),
            CancellationToken.None);

        (await repo.GetAsync(a, CancellationToken.None)).Elo.Should().Be(1512);
        (await repo.GetAsync(b, CancellationToken.None)).Elo.Should().Be(1488);
    }

    [Fact]
    public async Task Equal_ratings_draw_keeps_ratings_and_updates_stats()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        InMemoryRankingRepository repo = Seed(a, 1500, b, 1500);
        RankingService service = new(repo, new FakeClock(Now));

        await service.ApplyResultAsync(
            Game(a, b),
            Result(GameOutcomeKind.Draw, winnerKey: null),
            CancellationToken.None);

        RankingRecord after = await repo.GetAsync(a, CancellationToken.None);
        after.Elo.Should().Be(1500);
        after.Draws.Should().Be(1);
        after.GamesPlayed.Should().Be(1);
    }

    [Fact]
    public async Task Lower_rated_upset_has_pinned_delta()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        InMemoryRankingRepository repo = Seed(a, 1700, b, 1500);
        RankingService service = new(repo, new FakeClock(Now));

        await service.ApplyResultAsync(
            Game(a, b),
            Result(GameOutcomeKind.Win, winnerKey: 1),
            CancellationToken.None);

        (await repo.GetAsync(a, CancellationToken.None)).Elo.Should().Be(1682);
        (await repo.GetAsync(b, CancellationToken.None)).Elo.Should().Be(1518);
    }

    [Fact]
    public async Task Four_player_team_game_applies_same_delta_to_teammates()
    {
        Guid[] users = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        InMemoryRankingRepository repo = new(Now);
        foreach (Guid user in users)
        {
            repo.Set(new RankingRecord(user, 1500, 0, 0, 0, 0, Now));
        }

        RankingService service = new(repo, new FakeClock(Now));

        await service.ApplyResultAsync(
            Game(users, GameMode.FourPlayerTeams),
            Result(GameOutcomeKind.Win, winnerKey: 0),
            CancellationToken.None);

        (await repo.GetAsync(users[0], CancellationToken.None)).Elo.Should().Be(1512);
        (await repo.GetAsync(users[2], CancellationToken.None)).Elo.Should().Be(1512);
        (await repo.GetAsync(users[1], CancellationToken.None)).Elo.Should().Be(1488);
        (await repo.GetAsync(users[3], CancellationToken.None)).Elo.Should().Be(1488);
    }

    [Fact]
    public async Task Applying_same_game_twice_is_no_op()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        InMemoryRankingRepository repo = Seed(a, 1500, b, 1500);
        RankingService service = new(repo, new FakeClock(Now));
        GameRecord game = Game(a, b);
        GameResultRecord result = Result(GameOutcomeKind.Win, winnerKey: 0);

        await service.ApplyResultAsync(game, result, CancellationToken.None);
        await service.ApplyResultAsync(game, result, CancellationToken.None);

        (await repo.GetAsync(a, CancellationToken.None)).Elo.Should().Be(1512);
        (await repo.GetAsync(b, CancellationToken.None)).Elo.Should().Be(1488);
    }

    private static InMemoryRankingRepository Seed(Guid a, int eloA, Guid b, int eloB)
    {
        InMemoryRankingRepository repo = new(Now);
        repo.Set(new RankingRecord(a, eloA, 0, 0, 0, 0, Now));
        repo.Set(new RankingRecord(b, eloB, 0, 0, 0, 0, Now));
        return repo;
    }

    private static GameRecord Game(Guid a, Guid b) => Game([a, b], GameMode.TwoPlayer);

    private static GameRecord Game(Guid[] users, GameMode mode) =>
        new(
            Guid.NewGuid(),
            mode,
            "ranking",
            GameStatus.Finished,
            users[0],
            Now,
            Now,
            Now,
            ShuffleSeed: 1,
            StateSnapshotJson: "snapshot",
            BriscolaSuit: Suit.Bastoni,
            IsPrivate: false,
            PasswordHash: null,
            users.Select(u => (Guid?)u).ToImmutableArray(),
            Version: 0);

    private static GameResultRecord Result(GameOutcomeKind kind, int? winnerKey) =>
        new(Guid.NewGuid(), kind, winnerKey, "[60,60]", null, EndedReason.Normal);
}
