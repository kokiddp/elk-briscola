using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class InMemoryGameRepository : IGameRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, GameRecord> _games = [];
    private readonly List<MoveRecord> _moves = [];
    private readonly List<GameResultRecord> _results = [];

    public IReadOnlyList<MoveRecord> Moves => _moves;
    public IReadOnlyList<GameResultRecord> Results => _results;

    public Task<GameRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            _games.TryGetValue(id, out GameRecord? record);
            return Task.FromResult(record);
        }
    }

    public Task<IReadOnlyList<GameRecord>> ListByStatusAsync(GameStatus status, int take, CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<GameRecord> records = _games.Values
                .Where(g => g.Status == status)
                .OrderBy(g => g.CreatedAt)
                .Take(take)
                .ToArray();
            return Task.FromResult(records);
        }
    }

    public Task CreateAsync(GameRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            _games.Add(record.Id, record);
            return Task.CompletedTask;
        }
    }

    public Task<bool> UpdateAsync(GameRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(record.Id, out GameRecord? current))
            {
                return Task.FromResult(false);
            }

            if (current.Version != record.Version)
            {
                return Task.FromResult(false);
            }

            _games[record.Id] = record with { Version = record.Version + 1 };
            return Task.FromResult(true);
        }
    }

    public Task AppendMoveAsync(Guid gameId, MoveRecord move, CancellationToken ct)
    {
        lock (_gate)
        {
            _moves.Add(move);
            return Task.CompletedTask;
        }
    }

    public Task SaveResultAsync(GameResultRecord result, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_results.All(r => r.GameId != result.GameId))
            {
                _results.Add(result);
            }

            return Task.CompletedTask;
        }
    }

    public Task<int> GetNextMoveIndexAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate)
        {
            int max = _moves
                .Where(m => m.GameId == gameId)
                .Select(static m => (int?)m.MoveIndex)
                .Max() ?? -1;
            return Task.FromResult(max + 1);
        }
    }

    public Task<PagedResult<MatchHistoryRow>> ListHistoryForUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // The test double is used by Application-layer tests that don't
        // exercise history. Return an empty page so any consumer at least
        // compiles + observes deterministic, non-throwing behaviour.
        return Task.FromResult(new PagedResult<MatchHistoryRow>(
            Array.Empty<MatchHistoryRow>(),
            page < 1 ? 1 : page,
            pageSize < 1 ? 1 : pageSize,
            0));
    }
}
