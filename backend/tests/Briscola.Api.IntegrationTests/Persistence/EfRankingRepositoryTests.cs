using Briscola.Application.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Briscola.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Api.IntegrationTests.Persistence;

public sealed class EfRankingRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private async Task<Guid> CreateUserAsync()
    {
        using var scope = _fx.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userId = Guid.NewGuid();
        // Username allow-list is alphanumeric + _ -; the GUID's "n" form fits.
        var result = await users.CreateAsync(new ApplicationUser
        {
            Id = userId,
            UserName = $"u_{userId:N}"[..32],
            Email = $"{userId:N}@example.com",
            DisplayName = "Ranker",
            CreatedAt = _fx.Clock.UtcNow,
        }, "Strong-Pass-123");
        result.Succeeded.Should().BeTrue("create user must succeed; got {0}",
            string.Join(",", result.Errors.Select(e => e.Code)));
        return userId;
    }

    [Fact]
    public async Task GetAsync_auto_creates_default_ranking_on_first_read()
    {
        var userId = await CreateUserAsync();

        RankingRecord first;
        using (var scope = _fx.NewScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .GetAsync(userId, CancellationToken.None);
        }

        first.UserId.Should().Be(userId);
        first.Elo.Should().Be(1500);
        first.Wins.Should().Be(0);
        first.Losses.Should().Be(0);
        first.Draws.Should().Be(0);
        first.GamesPlayed.Should().Be(0);

        // Calling again returns the persisted row, not a fresh default.
        RankingRecord second;
        using (var scope = _fx.NewScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .GetAsync(userId, CancellationToken.None);
        }
        second.Should().Be(first);
    }

    [Fact]
    public async Task UpdateAsync_persists_new_values()
    {
        var userId = await CreateUserAsync();

        RankingRecord initial;
        using (var scope = _fx.NewScope())
        {
            initial = await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .GetAsync(userId, CancellationToken.None);
        }

        var updated = initial with
        {
            Elo = 1612,
            Wins = 1,
            GamesPlayed = 1,
            UpdatedAt = _fx.Clock.UtcNow.AddMinutes(5),
        };
        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .UpdateAsync(updated, CancellationToken.None);
        }

        using (var scope = _fx.NewScope())
        {
            var after = await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .GetAsync(userId, CancellationToken.None);
            after.Elo.Should().Be(1612);
            after.Wins.Should().Be(1);
            after.GamesPlayed.Should().Be(1);
        }
    }

    [Fact]
    public async Task HasProcessedGame_false_then_true_after_mark()
    {
        var gameId = Guid.NewGuid();

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfRankingRepository>();
            (await repo.HasProcessedGameAsync(gameId, CancellationToken.None)).Should().BeFalse();
        }

        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .MarkProcessedGameAsync(gameId, CancellationToken.None);
        }

        using (var scope = _fx.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<EfRankingRepository>();
            (await repo.HasProcessedGameAsync(gameId, CancellationToken.None)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task MarkProcessedGame_is_idempotent()
    {
        var gameId = Guid.NewGuid();

        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .MarkProcessedGameAsync(gameId, CancellationToken.None);
        }
        // Re-marking must not throw or duplicate.
        using (var scope = _fx.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfRankingRepository>()
                .MarkProcessedGameAsync(gameId, CancellationToken.None);
        }

        using (var scope = _fx.NewScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Briscola.Infrastructure.Persistence.BriscolaDbContext>();
            ctx.RankingProcessedGames.Should().ContainSingle();
        }
    }
}
