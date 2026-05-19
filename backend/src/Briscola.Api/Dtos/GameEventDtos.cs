using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Api.Dtos;

/// <summary>
/// Wire-format equivalent of <c>Briscola.Domain.Primitives.Card</c>.
/// Kept as a record so JSON enum serialization (suit/rank as strings)
/// works without bleeding domain attributes into the wire.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CardDto(Suit Suit, Rank Rank);

/// <summary>
/// Per-recipient redacted snapshot. Players see their own hand; everyone
/// else sees only counts. Spectators see counts only across all seats.
/// Mirrors <c>Briscola.Application.Orchestration.Events.RedactedStateForUser</c>;
/// duplicated here so the wire shape stays under <see cref="Briscola.Api"/>'s
/// control even if the application record changes.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record RedactedStateForUserDto(
    Guid GameId,
    GameMode Mode,
    GamePhase Phase,
    int DealerSeat,
    int LeaderSeat,
    int NextToPlaySeat,
    int TrickNumber,
    CardDto BriscolaCard,
    Suit BriscolaSuit,
    int StockCount,
    ImmutableArray<int> HandCountsBySeat,
    ImmutableArray<CardDto>? MyHand,
    ImmutableArray<CardDto>? MyPozzo,
    ImmutableArray<PlayedCardDto> CurrentTrick,
    ImmutableArray<int> SeatScores,
    GameOutcomeDto? Outcome,
    int? MySeatIndex,
    // Per-seat display name + Elo so the table / opponent area / end-
    // game dialog all render the same identity strings. Null entry =
    // seat empty (post-game-finish disconnect, mid-disconnect grace).
    ImmutableArray<PlayerInfoDto?> SeatPlayers,
    // UTC instant the NextToPlaySeat will auto-forfeit at; null when the
    // game isn't actively waiting on a move (eg. Finished). Resets on
    // every move so clients can render a per-turn countdown.
    DateTimeOffset? ActiveSeatForfeitDeadline);

[ExcludeFromCodeCoverage]
public sealed record PlayedCardDto(int SeatIndex, CardDto Card);

[ExcludeFromCodeCoverage]
public sealed record GameOutcomeDto(string Kind, int? WinnerKey);

[ExcludeFromCodeCoverage]
public sealed record CardPlayedDto(int SeatIndex, CardDto Card);

[ExcludeFromCodeCoverage]
public sealed record TrickResolvedDto(int WinnerSeat, ImmutableArray<int> NewSeatScores);

[ExcludeFromCodeCoverage]
public sealed record CardsDrawnDto(
    ImmutableArray<int> CountsBySeat,
    CardDto? DrawnCard);

[ExcludeFromCodeCoverage]
public sealed record GameFinishedDto(
    GameOutcomeDto Outcome,
    ImmutableArray<int> SeatScores,
    string Reason);

[ExcludeFromCodeCoverage]
public sealed record GameChatMessageDto(
    Guid Id,
    Guid GameId,
    Guid FromUserId,
    string FromUserName,
    string Text,
    DateTimeOffset CreatedAt);
