using System.Diagnostics.CodeAnalysis;

namespace Briscola.Infrastructure.Persistence.Entities;

/// <summary>
/// Per-seat row. Composite key (GameId, SeatIndex). UserId is nullable so
/// an Open game with unfilled seats can keep one row per seat from the
/// moment of creation; LobbyService promotes them to filled by writing
/// the UserId.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class GameSeatEntity
{
    public Guid GameId { get; set; }
    public int SeatIndex { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}
