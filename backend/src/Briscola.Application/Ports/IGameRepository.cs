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
}
