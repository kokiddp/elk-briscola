using System.Collections.Immutable;
using Briscola.Api.Dtos;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;
using Microsoft.AspNetCore.SignalR;

namespace Briscola.Api.Hubs;

/// <summary>
/// Bridges the application-layer <see cref="IGameEventBus"/> stream onto
/// SignalR's <see cref="IHubContext{THub, T}"/> for <see cref="GameHub"/>.
/// This is the <em>only</em> code that calls
/// <c>IHubContext&lt;GameHub, IGameClient&gt;</c> for game events; every
/// other component speaks to the bus and stays framework-agnostic.
///
/// Two routing patterns:
/// <list type="bullet">
///   <item><b>Targeted:</b> events that carry a <c>TargetUserId</c>
///   (Joined, StateUpdated, InvalidMoveRejected, the per-user
///   CardsDrawn) go to <c>Clients.User(userId)</c>.</item>
///   <item><b>Broadcast:</b> events keyed by <c>GameId</c> only
///   (CardPlayed, TrickResolved, PhaseChanged, GameFinished, the
///   disconnect/reconnect/idle telemetry) go to the
///   <c>game:{gameId}</c> group.</item>
/// </list>
/// Per-event handler exceptions are caught + logged so a single bad
/// event never tears down the dispatcher loop. Spectator-group support
/// (Phase 5.5) lands as an extra <c>Clients.Group(...)</c> call when
/// the spectator group is wired.
/// </summary>
public sealed partial class GameEventDispatcher : BackgroundService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Dispatch failed for {EventType} on game {GameId}")]
    private partial void LogDispatchFailed(Exception ex, string eventType, Guid gameId);

    private readonly IGameEventBus _bus;
    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly ILogger<GameEventDispatcher> _logger;

    public GameEventDispatcher(
        IGameEventBus bus,
        IHubContext<GameHub, IGameClient> hub,
        ILogger<GameEventDispatcher> logger)
    {
        _bus = bus;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (IGameEvent evt in _bus.ReadAllAsync(stoppingToken)
            .WithCancellation(stoppingToken)
            .ConfigureAwait(false))
        {
            try
            {
                await DispatchAsync(evt).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDispatchFailed(ex, evt.GetType().Name, evt.GameId);
            }
        }
    }

    private Task DispatchAsync(IGameEvent evt) => evt switch
    {
        JoinedEvent j =>
            _hub.Clients.User(j.TargetUserId.ToString()).Joined(ToDto(j.Snapshot)),
        StateUpdatedEvent s =>
            _hub.Clients.User(s.TargetUserId.ToString()).StateUpdated(ToDto(s.Snapshot)),
        CardPlayedEvent c =>
            _hub.Clients.Group(GameHub.GameGroup(c.GameId))
                .CardPlayed(new CardPlayedDto(c.SeatIndex, ToDto(c.Card))),
        TrickResolvedEvent t =>
            _hub.Clients.Group(GameHub.GameGroup(t.GameId))
                .TrickResolved(new TrickResolvedDto(t.WinnerSeat, t.NewSeatScores)),
        CardsDrawnEvent d => DispatchCardsDrawnAsync(d),
        PhaseChangedEvent p =>
            _hub.Clients.Group(GameHub.GameGroup(p.GameId))
                .PhaseChanged(p.NewPhase.ToString()),
        GameFinishedEvent f =>
            _hub.Clients.Group(GameHub.GameGroup(f.GameId))
                .GameFinished(new GameFinishedDto(
                    ToDto(f.Outcome),
                    f.SeatScores,
                    f.Reason.ToString())),
        PlayerDisconnectedEvent pd =>
            _hub.Clients.Group(GameHub.GameGroup(pd.GameId))
                .PlayerDisconnected(pd.SeatIndex, pd.GraceDeadlineUtc),
        PlayerReconnectedEvent pr =>
            _hub.Clients.Group(GameHub.GameGroup(pr.GameId))
                .PlayerReconnected(pr.SeatIndex),
        IdleWarningEvent iw =>
            _hub.Clients.Group(GameHub.GameGroup(iw.GameId))
                .IdleWarning(iw.SeatIndex, iw.ForfeitDeadlineUtc),
        InvalidMoveRejectedEvent im =>
            _hub.Clients.User(im.TargetUserId.ToString()).InvalidMove(im.Code.ToString()),
        ChatMessageEvent => Task.CompletedTask, // chat is broadcast directly by GameHub.SendChat
        _ => Task.CompletedTask,
    };

    private Task DispatchCardsDrawnAsync(CardsDrawnEvent evt)
    {
        // The room emits one CardsDrawnEvent per user, each with that
        // user's drawn card and TargetUserId set. We deliver the per-
        // user variant to that user only; everyone else in the group
        // sees the count change via the next StateUpdated.
        if (evt.TargetUserId is { } userId)
        {
            CardDto? drawn = evt.DrawnCard is { } card ? ToDto(card) : null;
            return _hub.Clients.User(userId.ToString())
                .CardsDrawn(new CardsDrawnDto(evt.CountsBySeat, drawn));
        }

        return Task.CompletedTask;
    }

    private static CardDto ToDto(Card card) => new(card.Suit, card.Rank);

    private static GameOutcomeDto ToDto(GameOutcome outcome) => outcome switch
    {
        GameOutcome.Winner w => new GameOutcomeDto("Winner", w.SeatOrTeam),
        GameOutcome.Draw => new GameOutcomeDto("Draw", null),
        _ => throw new InvalidOperationException("Unknown GameOutcome shape."),
    };

    private static RedactedStateForUserDto ToDto(RedactedStateForUser snapshot) =>
        new(
            snapshot.GameId,
            snapshot.Mode,
            snapshot.Phase,
            snapshot.DealerSeat,
            snapshot.LeaderSeat,
            snapshot.NextToPlaySeat,
            snapshot.TrickNumber,
            ToDto(snapshot.BriscolaCard),
            snapshot.BriscolaSuit,
            snapshot.StockCount,
            snapshot.HandCountsBySeat,
            snapshot.MyHand?.Select(ToDto).ToImmutableArray(),
            snapshot.MyPozzo?.Select(ToDto).ToImmutableArray(),
            snapshot.CurrentTrick.Select(p => new PlayedCardDto(p.SeatIndex, ToDto(p.Card))).ToImmutableArray(),
            snapshot.SeatScores,
            snapshot.Outcome is null ? null : ToDto(snapshot.Outcome));
}
