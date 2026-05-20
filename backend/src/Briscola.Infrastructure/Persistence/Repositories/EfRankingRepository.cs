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

    public async Task<IReadOnlyDictionary<Guid, RankingRecord>> GetManyAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        // Dedup before the round-trip so a 4p game whose seats happen to
        // include the same user twice (shouldn't happen, but the engine
        // accepts repeats) doesn't multiply SQL parameters.
        Guid[] distinct = userIds.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<Guid, RankingRecord>();
        }

        var existing = await db.Rankings.AsNoTracking()
            .Where(r => distinct.Contains(r.UserId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<Guid, RankingRecord> result = existing.ToDictionary(
            e => e.UserId,
            ToRecord);

        // Seed defaults for any id not already present — matches the
        // single-row GetAsync contract so callers can rely on a populated
        // entry for every id they asked about.
        Guid[] missing = distinct.Where(id => !result.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            DateTimeOffset now = clock.UtcNow;
            foreach (Guid id in missing)
            {
                RankingRecord fresh = RankingService.NewUserRanking(id, now);
                db.Rankings.Add(ToEntity(fresh));
                result[id] = fresh;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return result;
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
        // Race-free: just INSERT and treat a PK-conflict as success.
        // The previous SELECT-then-INSERT was a classic TOCTOU — two
        // concurrent finishers of the same game could both read
        // "doesn't exist" and both attempt the insert, the second one
        // blowing up on the PK uniqueness constraint. Single round-trip
        // happy path + a recovery path that detaches the tracked
        // entity so the change tracker doesn't keep retrying.
        RankingProcessedGameEntity entity = new() { GameId = gameId };
        db.RankingProcessedGames.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            bool alreadyMarked = await db.RankingProcessedGames
                .AsNoTracking()
                .AnyAsync(r => r.GameId == gameId, ct)
                .ConfigureAwait(false);
            db.Entry(entity).State = EntityState.Detached;
            if (!alreadyMarked)
            {
                // Some other DbUpdateException — rethrow so the caller
                // sees the real failure (FK violation, connection lost,
                // etc.) instead of silently swallowing it.
                throw;
            }
        }
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
