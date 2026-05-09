using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Persistence;

namespace Briscola.Infrastructure.Persistence.Entities;

[ExcludeFromCodeCoverage]
public sealed class GameMoveEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }

    /// <summary>Strictly-increasing per-game. Unique with GameId.</summary>
    public int MoveIndex { get; set; }

    public int SeatIndex { get; set; }
    public MoveType Type { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
