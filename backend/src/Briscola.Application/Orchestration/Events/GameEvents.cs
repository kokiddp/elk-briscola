using System.Collections.Immutable;
using Briscola.Application.Persistence;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Application.Orchestration.Events;

public interface IGameEvent
{
    Guid GameId { get; }
    DateTimeOffset At { get; }
}

public sealed record JoinedEvent(
    Guid GameId,
    DateTimeOffset At,
    RedactedStateForUser Snapshot,
    Guid TargetUserId) : IGameEvent;

public sealed record StateUpdatedEvent(
    Guid GameId,
    DateTimeOffset At,
    RedactedStateForUser Snapshot,
    Guid TargetUserId) : IGameEvent;

public sealed record CardPlayedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    Card Card) : IGameEvent;

public sealed record TrickResolvedEvent(
    Guid GameId,
    DateTimeOffset At,
    int WinnerSeat,
    ImmutableArray<int> NewSeatScores) : IGameEvent;

public sealed record CardsDrawnEvent(
    Guid GameId,
    DateTimeOffset At,
    ImmutableArray<int> CountsBySeat,
    Card? DrawnCard,
    Guid? TargetUserId) : IGameEvent;

public sealed record PhaseChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    GamePhase NewPhase) : IGameEvent;

public sealed record GameFinishedEvent(
    Guid GameId,
    DateTimeOffset At,
    GameOutcome Outcome,
    ImmutableArray<int> SeatScores,
    EndedReason Reason) : IGameEvent;

public sealed record PlayerDisconnectedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    DateTimeOffset GraceDeadlineUtc) : IGameEvent;

public sealed record PlayerReconnectedEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex) : IGameEvent;

public sealed record ChatMessageEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid FromUserId,
    string FromDisplayName,
    ChatScope Scope,
    string Text) : IGameEvent;

public sealed record InvalidMoveRejectedEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid TargetUserId,
    InvalidMoveCode Code) : IGameEvent;

public sealed record IdleWarningEvent(
    Guid GameId,
    DateTimeOffset At,
    int SeatIndex,
    DateTimeOffset ForfeitDeadlineUtc) : IGameEvent;

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
    GameOutcome? Outcome);
