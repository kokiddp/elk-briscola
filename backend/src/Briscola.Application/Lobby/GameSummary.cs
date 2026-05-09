using Briscola.Domain.Primitives;

namespace Briscola.Application.Lobby;

public sealed record GameSummary(
    Guid Id,
    GameMode Mode,
    string Name,
    GameStatus Status,
    int OccupiedSeats,
    int TotalSeats,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt);
