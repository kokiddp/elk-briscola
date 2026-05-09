using System.Collections.Immutable;
using Briscola.Application.Persistence;
using Briscola.Domain.Primitives;
using Briscola.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Api.IntegrationTests.Persistence;

public sealed class EfGameRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task GetNextMoveIndex_returns_zero_for_empty_log_then_increments()
    {
        var gameId = Guid.NewGuid();
        await CreateOpenGameAsync(gameId);

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfGameRepository>();
            (await repo.GetNextMoveIndexAsync(gameId, CancellationToken.None)).Should().Be(0);
        }

        for (int i = 0; i < 3; i++)
        {
            using var scope = _fx.NewScope();
            var repo = scope.ServiceProvider.GetRequiredService<EfGameRepository>();
            await repo.AppendMoveAsync(gameId, new MoveRecord(
                Id: Guid.NewGuid(),
                GameId: gameId,
                MoveIndex: i,
                SeatIndex: 0,
                Type: MoveType.PlayCard,
                PayloadJson: "{}",
                CreatedAt: _fx.Clock.UtcNow), CancellationToken.None);
        }

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfGameRepository>();
            (await repo.GetNextMoveIndexAsync(gameId, CancellationToken.None)).Should().Be(3);
        }
    }

    [Fact]
    public async Task UpdateAsync_with_matching_version_succeeds_and_bumps_version()
    {
        var gameId = Guid.NewGuid();
        await CreateOpenGameAsync(gameId);

        GameRecord? loaded;
        using (var scope = _fx.NewScope())
        {
            loaded = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None);
        }
        loaded.Should().NotBeNull();
        loaded!.Version.Should().Be(0);

        bool updated;
        using (var scope = _fx.NewScope())
        {
            updated = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .UpdateAsync(loaded with { Name = "renamed" }, CancellationToken.None);
        }
        updated.Should().BeTrue();

        using (var scope = _fx.NewScope())
        {
            var refetched = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None);
            refetched!.Name.Should().Be("renamed");
            refetched.Version.Should().Be(1);
        }
    }

    [Fact]
    public async Task UpdateAsync_with_stale_version_returns_false()
    {
        var gameId = Guid.NewGuid();
        await CreateOpenGameAsync(gameId);

        GameRecord stale;
        using (var scope = _fx.NewScope())
        {
            stale = (await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None))!;
        }

        // First update succeeds, bumping Version to 1.
        using (var scope = _fx.NewScope())
        {
            var ok = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .UpdateAsync(stale with { Name = "first-write" }, CancellationToken.None);
            ok.Should().BeTrue();
        }

        // Now `stale` still says Version=0; re-using it must return false.
        using (var scope = _fx.NewScope())
        {
            var second = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .UpdateAsync(stale with { Name = "second-write" }, CancellationToken.None);
            second.Should().BeFalse("the second writer is stale and must be rejected");
        }

        using (var scope = _fx.NewScope())
        {
            var refetched = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None);
            refetched!.Name.Should().Be("first-write");
            refetched.Version.Should().Be(1);
        }
    }

    [Fact]
    public async Task GetAsync_projects_seats_into_indexed_array()
    {
        var gameId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await CreateOpenGameAsync(gameId, seatUsers: ImmutableArray.Create<Guid?>(alice, bob));

        using var scope = _fx.NewScope();
        var record = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
            .GetAsync(gameId, CancellationToken.None);

        record!.SeatUserIds.Should().HaveCount(2);
        record.SeatUserIds[0].Should().Be(alice);
        record.SeatUserIds[1].Should().Be(bob);
    }

    [Fact]
    public async Task UpdateAsync_replaces_seat_user_ids()
    {
        var gameId = Guid.NewGuid();
        await CreateOpenGameAsync(gameId, seatUsers: ImmutableArray.Create<Guid?>(null, null));

        var alice = Guid.NewGuid();

        GameRecord loaded;
        using (var scope = _fx.NewScope())
        {
            loaded = (await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None))!;
        }

        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .UpdateAsync(loaded with { SeatUserIds = ImmutableArray.Create<Guid?>(alice, null) },
                    CancellationToken.None);
        }

        using (var scope = _fx.NewScope())
        {
            var after = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .GetAsync(gameId, CancellationToken.None);
            after!.SeatUserIds[0].Should().Be(alice);
            after.SeatUserIds[1].Should().BeNull();
        }
    }

    [Fact]
    public async Task ListByStatusAsync_filters_by_status_and_orders_by_CreatedAt()
    {
        var open1 = Guid.NewGuid();
        var open2 = Guid.NewGuid();
        await CreateOpenGameAsync(open1, name: "first", createdAt: _fx.Clock.UtcNow);
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await CreateOpenGameAsync(open2, name: "second", createdAt: _fx.Clock.UtcNow);

        using var scope = _fx.NewScope();
        var list = await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
            .ListByStatusAsync(GameStatus.Open, take: 100, CancellationToken.None);

        list.Select(g => g.Name).Should().Equal("first", "second");
    }

    [Fact]
    public async Task SaveResultAsync_is_idempotent()
    {
        var gameId = Guid.NewGuid();
        await CreateOpenGameAsync(gameId);

        var result = new GameResultRecord(
            gameId,
            GameOutcomeKind.Win,
            WinnerKey: 0,
            SeatScoresJson: "[60,60]",
            TeamScoresJson: null,
            EndedReason.Normal);

        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .SaveResultAsync(result, CancellationToken.None);
        }
        using (var scope = _fx.NewScope())
        {
            // Calling again must not throw or duplicate.
            await scope.ServiceProvider.GetRequiredService<EfGameRepository>()
                .SaveResultAsync(result, CancellationToken.None);
        }
        // Sanity: only one row.
        using (var scope = _fx.NewScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Briscola.Infrastructure.Persistence.BriscolaDbContext>();
            ctx.GameResults.Should().ContainSingle();
        }
    }

    private async Task CreateOpenGameAsync(
        Guid gameId,
        string name = "table",
        DateTimeOffset? createdAt = null,
        ImmutableArray<Guid?>? seatUsers = null)
    {
        using var scope = _fx.NewScope();
        var repo = scope.ServiceProvider.GetRequiredService<EfGameRepository>();
        await repo.CreateAsync(new GameRecord(
            Id: gameId,
            Mode: GameMode.TwoPlayer,
            Name: name,
            Status: GameStatus.Open,
            CreatedByUserId: Guid.NewGuid(),
            CreatedAt: createdAt ?? _fx.Clock.UtcNow,
            StartedAt: null,
            EndedAt: null,
            ShuffleSeed: 0,
            StateSnapshotJson: string.Empty,
            BriscolaSuit: Suit.Bastoni,
            IsPrivate: false,
            PasswordHash: null,
            SeatUserIds: seatUsers ?? ImmutableArray.Create<Guid?>(null, null),
            Version: 0), CancellationToken.None);
    }
}
