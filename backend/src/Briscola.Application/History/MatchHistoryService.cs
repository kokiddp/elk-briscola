using Briscola.Application.Persistence;
using Briscola.Application.Ports;

namespace Briscola.Application.History;

public sealed class MatchHistoryService(IGameRepository games)
{
    public Task SaveResultAsync(GameResultRecord result, CancellationToken ct) =>
        games.SaveResultAsync(result, ct);
}
