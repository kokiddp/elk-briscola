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

    /// <summary>
    /// Atomic combination of <see cref="UpdateAsync(GameRecord, CancellationToken)"/>
    /// and <see cref="AppendMoveAsync(Guid, MoveRecord, CancellationToken)"/>:
    /// the snapshot bump and the move-log row commit (or fail) together so a
    /// crash between them can't leave the persisted snapshot ahead of the
    /// move log (ADR 0005 — replay = ShuffleSeed + GameMoves; a missing move
    /// row breaks reproducibility of the snapshot). Returns false on
    /// optimistic-concurrency loss; never partially persists.
    /// </summary>
    Task<bool> UpdateAndAppendMoveAsync(
        GameRecord record,
        MoveRecord move,
        CancellationToken ct);
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
