using Briscola.Application.Persistence;

namespace Briscola.Application.Ports;

public interface IRankingRepository
{
    Task<RankingRecord> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Bulk lookup — one DB round-trip per call regardless of seat count.
    /// Returned dict is keyed by user id; entries are guaranteed for every
    /// id in <paramref name="userIds"/> (the underlying store seeds a
    /// 1500-Elo default on first read, same contract as
    /// <see cref="GetAsync(Guid, CancellationToken)"/>).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RankingRecord>> GetManyAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct);

    Task UpdateAsync(RankingRecord record, CancellationToken ct);
    Task<bool> HasProcessedGameAsync(Guid gameId, CancellationToken ct);
    Task MarkProcessedGameAsync(Guid gameId, CancellationToken ct);
}
