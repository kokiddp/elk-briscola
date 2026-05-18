using Briscola.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Persistence.Repositories;

/// <summary>
/// Looks up display names + current Elo for a batch of user ids by
/// joining <c>AspNetUsers</c> with the <c>Rankings</c> table. One SQL
/// round-trip per call, regardless of batch size. Users without a
/// rankings row default to the 1500 seed (so a brand-new account that
/// just sat down at a seat shows the right Elo on the lobby card before
/// they've played their first game).
/// </summary>
public sealed class EfPlayerDirectory(BriscolaDbContext db) : IPlayerDirectory
{
    private const int DefaultElo = 1500;

    public async Task<IReadOnlyDictionary<Guid, PlayerInfo>> GetAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        Guid[] ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PlayerInfo>();
        }

        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.UserName,
                Elo = db.Rankings
                    .Where(r => r.UserId == u.Id)
                    .Select(r => (int?)r.Elo)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<Guid, PlayerInfo> map = new(rows.Count);
        foreach (var row in rows)
        {
            string label = string.IsNullOrWhiteSpace(row.DisplayName)
                ? row.UserName ?? "Player"
                : row.DisplayName;
            map[row.Id] = new PlayerInfo(row.Id, label, row.Elo ?? DefaultElo);
        }
        return map;
    }
}
