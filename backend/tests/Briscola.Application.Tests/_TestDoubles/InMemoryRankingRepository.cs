using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Application.Ranking;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class InMemoryRankingRepository(DateTimeOffset now) : IRankingRepository
{
    private readonly Dictionary<Guid, RankingRecord> _rankings = [];
    private readonly HashSet<Guid> _processedGames = [];

    public void Set(RankingRecord record) => _rankings[record.UserId] = record;

    public Task<RankingRecord> GetAsync(Guid userId, CancellationToken ct)
    {
        if (!_rankings.TryGetValue(userId, out RankingRecord? record))
        {
            record = RankingService.NewUserRanking(userId, now);
            _rankings[userId] = record;
        }

        return Task.FromResult(record);
    }

    public Task UpdateAsync(RankingRecord record, CancellationToken ct)
    {
        _rankings[record.UserId] = record;
        return Task.CompletedTask;
    }

    public Task<bool> HasProcessedGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(_processedGames.Contains(gameId));

    public Task MarkProcessedGameAsync(Guid gameId, CancellationToken ct)
    {
        _processedGames.Add(gameId);
        return Task.CompletedTask;
    }
}
