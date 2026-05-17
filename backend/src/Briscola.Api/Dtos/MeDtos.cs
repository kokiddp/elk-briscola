using System.Diagnostics.CodeAnalysis;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record MeResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string ActiveCardSetId,
    RankingDto Ranking);

[ExcludeFromCodeCoverage]
public sealed record MePatchRequest(
    string? DisplayName,
    string? ActiveCardSetId);

[ExcludeFromCodeCoverage]
public sealed record RankingDto(
    int Elo,
    int Wins,
    int Losses,
    int Draws,
    int GamesPlayed,
    DateTimeOffset UpdatedAt);

[ExcludeFromCodeCoverage]
public sealed record MatchHistoryEntryDto(
    Guid GameId,
    Briscola.Domain.Primitives.GameMode Mode,
    string Name,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int MySeatIndex,
    System.Collections.Immutable.ImmutableArray<Guid?> SeatUserIds,
    string OutcomeKind,
    int? WinnerKey,
    System.Collections.Immutable.ImmutableArray<int> SeatScores,
    System.Collections.Immutable.ImmutableArray<int>? TeamScores,
    string Reason);

[ExcludeFromCodeCoverage]
public sealed record MatchHistoryPageDto(
    System.Collections.Generic.IReadOnlyList<MatchHistoryEntryDto> Items,
    int Page,
    int PageSize,
    long TotalCount);
