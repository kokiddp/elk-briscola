using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record CreateGameRequestDto(
    GameMode Mode,
    // Name is now optional; the UI doesn't collect one. Older clients
    // and integration tests still send a string, so the wire stays
    // backwards-compatible. Server-side, an empty/null value is
    // replaced by an empty placeholder so the GameRecord.Name column
    // (non-null) is satisfied without forcing a migration.
    string? Name,
    bool IsPrivate,
    string? Password);

[ExcludeFromCodeCoverage]
public sealed record JoinGameRequestDto(string? Password);

[ExcludeFromCodeCoverage]
public sealed record PlayerInfoDto(Guid UserId, string DisplayName, int Elo);

[ExcludeFromCodeCoverage]
public sealed record GameSummaryDto(
    Guid Id,
    GameMode Mode,
    string Name,
    GameStatus Status,
    int OccupiedSeats,
    int TotalSeats,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    // Positional, one entry per seat. Null where the seat is still
    // empty. The SPA renders display names + Elos on the lobby card.
    ImmutableArray<PlayerInfoDto?> SeatPlayers);

[ExcludeFromCodeCoverage]
public sealed record GameDetailDto(
    Guid Id,
    GameMode Mode,
    string Name,
    GameStatus Status,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    ImmutableArray<Guid?> Seats);
