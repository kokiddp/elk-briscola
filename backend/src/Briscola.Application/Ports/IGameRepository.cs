using Briscola.Application.Persistence;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Ports;

public interface IGameRepository
{
    Task<GameRecord?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<GameRecord>> ListByStatusAsync(GameStatus status, int take, CancellationToken ct);
    Task CreateAsync(GameRecord record, CancellationToken ct);
    Task<bool> UpdateAsync(GameRecord record, CancellationToken ct);
    Task AppendMoveAsync(Guid gameId, MoveRecord move, CancellationToken ct);
    Task SaveResultAsync(GameResultRecord result, CancellationToken ct);

    /// <summary>
    /// Returns the next free <c>MoveIndex</c> for this game (i.e. one more than
    /// the highest existing index, or 0 if no moves have been recorded yet).
    /// Used by <see cref="Orchestration.GameRoom"/> when rehydrating from a
    /// snapshot so the move log doesn't restart at 0 and collide with existing
    /// rows on the unique <c>(GameId, MoveIndex)</c> index.
    /// </summary>
    Task<int> GetNextMoveIndexAsync(Guid gameId, CancellationToken ct);

    /// <summary>
    /// Paginated history of Finished games the given user was seated at,
    /// newest first. Returns the total count so the UI can render a pager.
    /// Page is 1-based.
    /// </summary>
    Task<PagedResult<MatchHistoryRow>> ListHistoryForUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct);
}
