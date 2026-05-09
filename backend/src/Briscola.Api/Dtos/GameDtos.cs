using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record CreateGameRequestDto(
    GameMode Mode,
    string Name,
    bool IsPrivate,
    string? Password);

[ExcludeFromCodeCoverage]
public sealed record JoinGameRequestDto(string? Password);

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
    DateTimeOffset? StartedAt);

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
