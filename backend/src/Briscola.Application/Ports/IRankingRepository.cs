using Briscola.Application.Persistence;

namespace Briscola.Application.Ports;

public interface IRankingRepository
{
    Task<RankingRecord> GetAsync(Guid userId, CancellationToken ct);
    Task UpdateAsync(RankingRecord record, CancellationToken ct);
    Task<bool> HasProcessedGameAsync(Guid gameId, CancellationToken ct);
    Task MarkProcessedGameAsync(Guid gameId, CancellationToken ct);
}
