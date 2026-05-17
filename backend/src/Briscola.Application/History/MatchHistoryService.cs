using Briscola.Application.Persistence;
using Briscola.Application.Ports;

namespace Briscola.Application.History;

public sealed class MatchHistoryService(IGameRepository games)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public Task SaveResultAsync(GameResultRecord result, CancellationToken ct) =>
        games.SaveResultAsync(result, ct);

    /// <summary>
    /// Paginated history of Finished games for <paramref name="userId"/>,
    /// newest first. Inputs are clamped: <paramref name="page"/> &gt;= 1,
    /// <paramref name="pageSize"/> in [1, <see cref="MaxPageSize"/>].
    /// </summary>
    public Task<PagedResult<MatchHistoryRow>> ListForUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        int clampedPage = page < 1 ? 1 : page;
        int clampedSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return games.ListHistoryForUserAsync(userId, clampedPage, clampedSize, ct);
    }
}
