using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Application.Ranking;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Persistence.Repositories;

public sealed class EfRankingRepository(BriscolaDbContext db, IClock clock) : IRankingRepository
{
    public async Task<RankingRecord> GetAsync(Guid userId, CancellationToken ct)
    {
        var entity = await db.Rankings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            .ConfigureAwait(false);
        if (entity is not null)
        {
            return ToRecord(entity);
        }

        // Auto-create a default ranking on first read. Mirrors the Phase 2
        // InMemoryRankingRepository behaviour so RankingService can compute
        // a delta against a fresh-default rating without a separate insert.
        var fresh = RankingService.NewUserRanking(userId, clock.UtcNow);
        db.Rankings.Add(ToEntity(fresh));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fresh;
    }

    public async Task UpdateAsync(RankingRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        var entity = await db.Rankings.FirstOrDefaultAsync(r => r.UserId == record.UserId, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            db.Rankings.Add(ToEntity(record));
        }
        else
        {
            entity.Elo = record.Elo;
            entity.Wins = record.Wins;
            entity.Losses = record.Losses;
            entity.Draws = record.Draws;
            entity.GamesPlayed = record.GamesPlayed;
            entity.UpdatedAt = record.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HasProcessedGameAsync(Guid gameId, CancellationToken ct)
    {
        return await db.RankingProcessedGames.AsNoTracking()
            .AnyAsync(r => r.GameId == gameId, ct)
            .ConfigureAwait(false);
    }

    public async Task MarkProcessedGameAsync(Guid gameId, CancellationToken ct)
    {
        var exists = await db.RankingProcessedGames
            .AnyAsync(r => r.GameId == gameId, ct)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        db.RankingProcessedGames.Add(new RankingProcessedGameEntity { GameId = gameId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static RankingRecord ToRecord(RankingEntity e) =>
        new(e.UserId, e.Elo, e.Wins, e.Losses, e.Draws, e.GamesPlayed, e.UpdatedAt);

    private static RankingEntity ToEntity(RankingRecord r) =>
        new()
        {
            UserId = r.UserId,
            Elo = r.Elo,
            Wins = r.Wins,
            Losses = r.Losses,
            Draws = r.Draws,
            GamesPlayed = r.GamesPlayed,
            UpdatedAt = r.UpdatedAt,
        };
}
