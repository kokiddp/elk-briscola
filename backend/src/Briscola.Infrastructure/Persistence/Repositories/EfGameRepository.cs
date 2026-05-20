using System.Collections.Immutable;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Persistence.Repositories;

public sealed class EfGameRepository(BriscolaDbContext db) : IGameRepository
{
    public async Task<GameRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Games.AsNoTracking()
            .Include(g => g.Seats)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IReadOnlyList<GameRecord>> ListByStatusAsync(GameStatus status, int take, CancellationToken ct)
    {
        if (take < 1)
        {
            return [];
        }

        var rows = await db.Games.AsNoTracking()
            .Include(g => g.Seats)
            .Where(g => g.Status == status)
            .OrderBy(g => g.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<GameRecord>> ListExpiredOpenAsync(
        DateTimeOffset cutoff,
        int take,
        CancellationToken ct)
    {
        if (take < 1)
        {
            return [];
        }

        // SQL filter on (Status, CreatedAt) — uses the existing IX_Games_Status
        // + IX_Games_CreatedAt indexes. Without this the janitor used to pull
        // every open row into memory each minute and filter client-side.
        var rows = await db.Games.AsNoTracking()
            .Include(g => g.Seats)
            .Where(g => g.Status == GameStatus.Open && g.CreatedAt < cutoff)
            .OrderBy(g => g.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public async Task CreateAsync(GameRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        db.Games.Add(ToEntity(record));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(GameRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Match-and-bump on Version to implement optimistic concurrency. We
        // load the row, check Version, and (if it matches) overwrite scalar
        // columns and bump Version inside a single SaveChanges call. EF's
        // change tracker buys us atomicity within the transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var entity = await db.Games
            .Include(g => g.Seats)
            .FirstOrDefaultAsync(g => g.Id == record.Id, ct)
            .ConfigureAwait(false);
        if (entity is null || entity.Version != record.Version)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        // Scalar fields.
        entity.Mode = record.Mode;
        entity.Name = record.Name;
        entity.Status = record.Status;
        entity.CreatedByUserId = record.CreatedByUserId;
        entity.CreatedAt = record.CreatedAt;
        entity.StartedAt = record.StartedAt;
        entity.EndedAt = record.EndedAt;
        entity.ShuffleSeed = record.ShuffleSeed;
        entity.StateSnapshotJson = record.StateSnapshotJson;
        entity.BriscolaSuit = record.BriscolaSuit;
        entity.IsPrivate = record.IsPrivate;
        entity.PasswordHash = record.PasswordHash;
        entity.Version = record.Version + 1;

        // Seats: replace the set. The application contract talks in
        // `ImmutableArray<Guid?>` indexed by seat. We add missing seats,
        // update existing ones, and leave none dangling.
        for (int seat = 0; seat < record.SeatUserIds.Length; seat++)
        {
            var existing = entity.Seats.FirstOrDefault(s => s.SeatIndex == seat);
            if (existing is null)
            {
                entity.Seats.Add(new GameSeatEntity
                {
                    GameId = entity.Id,
                    SeatIndex = seat,
                    UserId = record.SeatUserIds[seat],
                    JoinedAt = record.CreatedAt,
                });
            }
            else
            {
                existing.UserId = record.SeatUserIds[seat];
            }
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }
    }

    public async Task AppendMoveAsync(Guid gameId, MoveRecord move, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(move);
        db.GameMoves.Add(NewMoveEntity(gameId, move));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAndAppendMoveAsync(
        GameRecord record,
        MoveRecord move,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(move);

        // Same match-and-bump shape as UpdateAsync, except we also stage
        // the GameMove insert so both rows commit in one SaveChanges +
        // one transaction. A crash between the snapshot bump and the
        // move-log insert can no longer leave the persisted snapshot
        // ahead of the move log (the replay invariant from ADR 0005).
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var entity = await db.Games
            .Include(g => g.Seats)
            .FirstOrDefaultAsync(g => g.Id == record.Id, ct)
            .ConfigureAwait(false);
        if (entity is null || entity.Version != record.Version)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        ApplyScalarChanges(entity, record);
        ReplaceSeats(entity, record);
        db.GameMoves.Add(NewMoveEntity(record.Id, move));

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }
    }

    private static GameMoveEntity NewMoveEntity(Guid gameId, MoveRecord move) => new()
    {
        Id = move.Id,
        GameId = gameId,
        MoveIndex = move.MoveIndex,
        SeatIndex = move.SeatIndex,
        Type = move.Type,
        PayloadJson = move.PayloadJson,
        CreatedAt = move.CreatedAt,
    };

    private static void ApplyScalarChanges(GameEntity entity, GameRecord record)
    {
        entity.Mode = record.Mode;
        entity.Name = record.Name;
        entity.Status = record.Status;
        entity.CreatedByUserId = record.CreatedByUserId;
        entity.CreatedAt = record.CreatedAt;
        entity.StartedAt = record.StartedAt;
        entity.EndedAt = record.EndedAt;
        entity.ShuffleSeed = record.ShuffleSeed;
        entity.StateSnapshotJson = record.StateSnapshotJson;
        entity.BriscolaSuit = record.BriscolaSuit;
        entity.IsPrivate = record.IsPrivate;
        entity.PasswordHash = record.PasswordHash;
        entity.Version = record.Version + 1;
    }

    private static void ReplaceSeats(GameEntity entity, GameRecord record)
    {
        for (int seat = 0; seat < record.SeatUserIds.Length; seat++)
        {
            var existing = entity.Seats.FirstOrDefault(s => s.SeatIndex == seat);
            if (existing is null)
            {
                entity.Seats.Add(new GameSeatEntity
                {
                    GameId = entity.Id,
                    SeatIndex = seat,
                    UserId = record.SeatUserIds[seat],
                    JoinedAt = record.CreatedAt,
                });
            }
            else
            {
                existing.UserId = record.SeatUserIds[seat];
            }
        }
    }

    public async Task SaveResultAsync(GameResultRecord result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        var existing = await db.GameResults.FirstOrDefaultAsync(r => r.GameId == result.GameId, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // Idempotent: SaveResult is called once per game, but a retry on
            // an interrupted transaction must not duplicate the row.
            return;
        }

        db.GameResults.Add(new GameResultEntity
        {
            GameId = result.GameId,
            Kind = result.Kind,
            WinnerKey = result.WinnerKey,
            SeatScoresJson = result.SeatScoresJson,
            TeamScoresJson = result.TeamScoresJson,
            Reason = result.Reason,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> GetNextMoveIndexAsync(Guid gameId, CancellationToken ct)
    {
        var max = await db.GameMoves.AsNoTracking()
            .Where(m => m.GameId == gameId)
            .Select(m => (int?)m.MoveIndex)
            .MaxAsync(ct)
            .ConfigureAwait(false);
        return (max ?? -1) + 1;
    }

    public async Task<PagedResult<MatchHistoryRow>> ListHistoryForUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // Defensive clamps — controllers also validate, but a missized
        // page from a test or a stale client shouldn't blow up.
        int clampedPage = page < 1 ? 1 : page;
        int clampedSize = pageSize < 1 ? 1 : (pageSize > 100 ? 100 : pageSize);

        // Identify all Finished games the user was seated at.
        IQueryable<GameEntity> baseQuery = db.Games.AsNoTracking()
            .Where(g => g.Status == GameStatus.Finished)
            .Where(g => g.Seats.Any(s => s.UserId == userId));

        long total = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        // ThenBy(Id) gives a deterministic tiebreaker when two games
        // finished in the same tick — without it pagination can skip or
        // duplicate rows across pages.
        // AsSplitQuery sidesteps EF's row-multiplication warning when
        // paginating across a one-to-many Include — seats are then loaded
        // via a second SELECT keyed by the paged game IDs.
        var rows = await baseQuery
            .Include(g => g.Seats)
            .OrderByDescending(g => g.EndedAt ?? g.StartedAt ?? g.CreatedAt)
            .ThenByDescending(g => g.Id)
            .Skip((clampedPage - 1) * clampedSize)
            .Take(clampedSize)
            .AsSplitQuery()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Batch-load matching results so we don't N+1.
        Guid[] gameIds = rows.Select(g => g.Id).ToArray();
        Dictionary<Guid, GameResultEntity> resultsById = await db.GameResults.AsNoTracking()
            .Where(r => gameIds.Contains(r.GameId))
            .ToDictionaryAsync(r => r.GameId, ct)
            .ConfigureAwait(false);

        List<MatchHistoryRow> items = new(rows.Count);
        foreach (GameEntity g in rows)
        {
            // mySeat is guaranteed non-null by the WHERE Seats.Any(...) above.
            GameSeatEntity mySeat = g.Seats.First(s => s.UserId == userId);

            int totalSeats = g.Seats.Count == 0
                ? PlayerCount(g.Mode)
                : Math.Max(g.Seats.Max(s => s.SeatIndex) + 1, PlayerCount(g.Mode));
            Guid?[] seatUserIds = new Guid?[totalSeats];
            foreach (GameSeatEntity s in g.Seats)
            {
                if (s.SeatIndex >= 0 && s.SeatIndex < totalSeats)
                {
                    seatUserIds[s.SeatIndex] = s.UserId;
                }
            }

            if (!resultsById.TryGetValue(g.Id, out GameResultEntity? result))
            {
                // Finished without a result row would be a bug, but we'd rather
                // hide the entry than throw. The grafana dashboard catches
                // orphans separately.
                continue;
            }

            items.Add(new MatchHistoryRow(
                g.Id,
                g.Mode,
                g.Name,
                g.StartedAt,
                g.EndedAt,
                mySeat.SeatIndex,
                [.. seatUserIds],
                result.Kind,
                result.WinnerKey,
                result.SeatScoresJson,
                result.TeamScoresJson,
                result.Reason));
        }

        return new PagedResult<MatchHistoryRow>(items, clampedPage, clampedSize, total);
    }

    private static GameRecord ToRecord(GameEntity e)
    {
        // Project seats into a positional ImmutableArray<Guid?>. If a seat
        // row is missing (shouldn't happen post-create), fall back to null.
        int totalSeats = e.Seats.Count == 0
            ? PlayerCount(e.Mode)
            : Math.Max(e.Seats.Max(s => s.SeatIndex) + 1, PlayerCount(e.Mode));
        var seats = new Guid?[totalSeats];
        foreach (var s in e.Seats)
        {
            if (s.SeatIndex >= 0 && s.SeatIndex < totalSeats)
            {
                seats[s.SeatIndex] = s.UserId;
            }
        }

        return new GameRecord(
            e.Id,
            e.Mode,
            e.Name,
            e.Status,
            e.CreatedByUserId,
            e.CreatedAt,
            e.StartedAt,
            e.EndedAt,
            e.ShuffleSeed,
            e.StateSnapshotJson,
            e.BriscolaSuit,
            e.IsPrivate,
            e.PasswordHash,
            seats.ToImmutableArray(),
            e.Version);
    }

    private static GameEntity ToEntity(GameRecord r) => new()
    {
        Id = r.Id,
        Mode = r.Mode,
        Name = r.Name,
        Status = r.Status,
        CreatedByUserId = r.CreatedByUserId,
        CreatedAt = r.CreatedAt,
        StartedAt = r.StartedAt,
        EndedAt = r.EndedAt,
        ShuffleSeed = r.ShuffleSeed,
        StateSnapshotJson = r.StateSnapshotJson,
        BriscolaSuit = r.BriscolaSuit,
        IsPrivate = r.IsPrivate,
        PasswordHash = r.PasswordHash,
        Version = r.Version,
        Seats = [.. Enumerable.Range(0, r.SeatUserIds.Length).Select(seat => new GameSeatEntity
        {
            GameId = r.Id,
            SeatIndex = seat,
            UserId = r.SeatUserIds[seat],
            JoinedAt = r.CreatedAt,
        })],
    };

    private static int PlayerCount(GameMode mode) => mode switch
    {
        GameMode.TwoPlayer => 2,
        GameMode.FourPlayerTeams => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode"),
    };
}
