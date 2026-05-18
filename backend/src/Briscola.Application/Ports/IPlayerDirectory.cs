using System.Collections.Immutable;

namespace Briscola.Application.Ports;

/// <summary>
/// Snapshot of a player's user-facing identity + current ranking — what
/// the lobby card and the game table both want to show next to a seat.
/// Carried over the wire as part of <c>GameSummary</c> + the redacted
/// game-state snapshot so the SPA doesn't need a separate /users lookup.
/// </summary>
public sealed record PlayerInfo(Guid UserId, string DisplayName, int Elo);

/// <summary>
/// Resolves <see cref="PlayerInfo"/> snapshots for a batch of user ids.
/// Implemented in the Infrastructure layer over AspNet Identity + the
/// Rankings table. Missing users (e.g. deleted account that still sits in
/// a finished game's seats) are simply absent from the returned map.
/// </summary>
public interface IPlayerDirectory
{
    Task<IReadOnlyDictionary<Guid, PlayerInfo>> GetAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct);
}
