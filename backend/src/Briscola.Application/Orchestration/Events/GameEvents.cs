using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Lobby;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Application.Orchestration.Events;

public interface IGameEvent
{
    Guid GameId { get; }
    DateTimeOffset At { get; }
}

[ExcludeFromCodeCoverage]
public sealed record JoinedEvent(
    Guid GameId,
    DateTimeOffset At,
    RedactedStateForUser Snapshot,
    Guid TargetUserId) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record StateUpdatedEvent(
    Guid GameId,
    DateTimeOffset At,
    RedactedStateForUser Snapshot,
    Guid? TargetUserId) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record CardPlayedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    Card Card) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record TrickResolvedEvent(
    Guid GameId,
    DateTimeOffset At,
    int WinnerSeat,
    ImmutableArray<int> NewSeatScores) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record CardsDrawnEvent(
    Guid GameId,
    DateTimeOffset At,
    ImmutableArray<int> CountsBySeat,
    Card? DrawnCard,
    Guid? TargetUserId) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record PhaseChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    GamePhase NewPhase) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record GameFinishedEvent(
    Guid GameId,
    DateTimeOffset At,
    GameOutcome Outcome,
    ImmutableArray<int> SeatScores,
    EndedReason Reason) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record PlayerDisconnectedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    DateTimeOffset GraceDeadlineUtc) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record PlayerReconnectedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record ChatMessageEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid FromUserId,
    string FromDisplayName,
    ChatScope Scope,
    string Text) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record InvalidMoveRejectedEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid TargetUserId,
    InvalidMoveCode Code) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record IdleWarningEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    DateTimeOffset ForfeitDeadlineUtc) : IGameEvent;

/// <summary>
/// Emitted after <c>RankingService.ApplyResultAsync</c> updates a single
/// player's ranking. The dispatcher routes one of these to the affected
/// user over their personal SignalR connection so the SPA can patch its
/// cached <c>/me</c> snapshot without polling. Carries the full
/// <c>RankingRecord</c> (Elo, W/L/D, games played) so the client doesn't
/// need a separate REST round-trip.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record RankingUpdatedEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid TargetUserId,
    RankingRecord Ranking) : IGameEvent;

/// <summary>
/// Lifecycle telemetry for the lobby UI: created/seat-fill/started/ended.
/// The hub dispatcher fans these to the <c>lobby:open</c> SignalR group.
/// </summary>
// Lobby summary events now carry the pre-enriched GameSummary
// (with per-seat display names + Elos) so the dispatcher doesn't
// need its own scoped IPlayerDirectory lookup on every push.
[ExcludeFromCodeCoverage]
public sealed record LobbyGameCreatedEvent(Guid GameId, DateTimeOffset At, GameSummary Summary) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record LobbyGameUpdatedEvent(Guid GameId, DateTimeOffset At, GameSummary Summary) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record LobbyGameStartedEvent(Guid GameId, DateTimeOffset At) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record LobbyGameEndedEvent(Guid GameId, DateTimeOffset At) : IGameEvent;

[ExcludeFromCodeCoverage]
public sealed record RedactedStateForUser(
    Guid GameId,
    GameMode Mode,
    GamePhase Phase,
    int DealerSeat,
    int LeaderSeat,
    int NextToPlaySeat,
    int TrickNumber,
    Card BriscolaCard,
    Suit BriscolaSuit,
    int StockCount,
    ImmutableArray<int> HandCountsBySeat,
    ImmutableArray<Card>? MyHand,
    ImmutableArray<Card>? MyPozzo,
    ImmutableArray<PlayedCard> CurrentTrick,
    ImmutableArray<int> SeatScores,
    GameOutcome? Outcome,
    int? MySeatIndex);
