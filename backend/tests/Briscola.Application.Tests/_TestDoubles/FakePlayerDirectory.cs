using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

/// <summary>
/// Stub <see cref="IPlayerDirectory"/> for Application-layer tests that
/// don't exercise the display-name / Elo enrichment. Returns a canned
/// <c>PlayerInfo</c> for any user id passed in so callers can assert
/// on shape without setting up a UserManager.
/// </summary>
internal sealed class FakePlayerDirectory : IPlayerDirectory
{
    public Task<IReadOnlyDictionary<Guid, PlayerInfo>> GetAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, PlayerInfo> map = userIds
            .Distinct()
            .ToDictionary(id => id, id => new PlayerInfo(id, $"player-{id:N}".Substring(0, 14), 1500));
        return Task.FromResult(map);
    }
}
