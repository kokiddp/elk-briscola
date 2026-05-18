using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Lobby;

[ExcludeFromCodeCoverage]
public sealed record GameSummary(
    Guid Id,
    GameMode Mode,
    string Name,
    GameStatus Status,
    int OccupiedSeats,
    int TotalSeats,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    // Positional, one entry per seat. Null where a seat is still empty.
    // Display name + current Elo so the lobby card can show who's
    // already at the table.
    ImmutableArray<PlayerInfo?> SeatPlayers);
