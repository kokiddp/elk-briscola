using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Infrastructure.Persistence.Entities;

/// <summary>
/// Mirrors <see cref="Briscola.Application.Persistence.GameRecord"/>.
/// <see cref="Version"/> is the application-managed optimistic-concurrency
/// token (NOT EF's <c>byte[] RowVersion</c> / Postgres <c>xmin</c>) — see
/// AGENTS.md § Persistence and the README schema notes.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class GameEntity
{
    public Guid Id { get; set; }
    public GameMode Mode { get; set; }
    public string Name { get; set; } = string.Empty;
    public GameStatus Status { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long ShuffleSeed { get; set; }
    public string StateSnapshotJson { get; set; } = string.Empty;
    public Suit BriscolaSuit { get; set; }
    public bool IsPrivate { get; set; }
    public string? PasswordHash { get; set; }
    public long Version { get; set; }

    public List<GameSeatEntity> Seats { get; set; } = [];
}
