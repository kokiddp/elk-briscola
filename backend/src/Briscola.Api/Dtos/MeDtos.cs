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
