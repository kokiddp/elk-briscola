using System.Collections.Immutable;
using System.Threading.Channels;
using Briscola.Application.Configuration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Orchestration.Timers;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Engine;
using Briscola.Domain.Errors;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Orchestration;

public sealed class GameRoom
{
    private readonly IGameRepository _games;
    private readonly IGameStateCodec _stateCodec;
    private readonly IBriscolaEngine _engine;
    private readonly IGameEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ITimerService _timers;
    private readonly GameOptions _options;
    private readonly Channel<QueuedCommand> _commands =
        Channel.CreateUnbounded<QueuedCommand>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Dictionary<Guid, int> _userIdToSeat = [];
    private readonly Dictionary<int, Guid> _seatToUserId = [];
    private readonly Dictionary<int, ConnectionStatus> _seatStatuses = [];
    private readonly Dictionary<int, IDisposable> _reconnectTimers = [];
    private readonly List<IDisposable> _idleTimers = [];

    private GameRecord _record;
    private GameState _state;
    private int _moveIndex;
    private DateTimeOffset _lastMoveCompletedAt;
    private int? _idleWarnedForSeat;

    private GameRoom(
        GameRecord record,
        GameState state,
        IGameRepository games,
        IGameStateCodec stateCodec,
        IBriscolaEngine engine,
        IGameEventBus eventBus,
        IClock clock,
        ITimerService timers,
        IOptions<GameOptions> options)
    {
        _record = record;
        _state = state;
        _games = games;
        _stateCodec = stateCodec;
        _engine = engine;
        _eventBus = eventBus;
        _clock = clock;
        _timers = timers;
        _options = options.Value;
        _lastMoveCompletedAt = clock.UtcNow;

        for (int seat = 0; seat < record.SeatUserIds.Length; seat++)
        {
            Guid? userId = record.SeatUserIds[seat];
            if (userId is null)
            {
                continue;
            }

            _userIdToSeat[userId.Value] = seat;
            _seatToUserId[seat] = userId.Value;
            _seatStatuses[seat] = ConnectionStatus.Connected;
        }

        ScheduleIdleChecks();
        _ = Task.Run(ProcessLoopAsync);
    }

    public GameState CurrentState => _state;

    public static GameRoom FromRecord(
        GameRecord record,
        IGameRepository games,
        IGameStateCodec stateCodec,
        IBriscolaEngine engine,
        IGameEventBus eventBus,
        IClock clock,
        ITimerService timers,
        IOptions<GameOptions> options)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.StateSnapshotJson))
        {
            throw new ArgumentException("A running game room requires a state snapshot.", nameof(record));
        }

        GameState state = stateCodec.Deserialize(record.StateSnapshotJson);
        return new GameRoom(record, state, games, stateCodec, engine, eventBus, clock, timers, options);
    }

    public async Task EnqueueAsync(GameCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new QueuedCommand(cmd, completion), ct).ConfigureAwait(false);
        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessLoopAsync()
    {
        await foreach (QueuedCommand queued in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await ApplyAsync(queued.Command, CancellationToken.None).ConfigureAwait(false);
                queued.Completion.SetResult();
            }
            catch (Exception ex)
            {
                queued.Completion.SetException(ex);
            }
        }
    }

    private async Task ApplyAsync(GameCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case PlayCardCommand play:
                await PlayCardAsync(play, ct).ConfigureAwait(false);
                break;
            case ViewOwnPileCommand view:
                await ViewOwnPileAsync(view, ct).ConfigureAwait(false);
                break;
            case DisconnectCommand disconnect:
                await DisconnectAsync(disconnect, ct).ConfigureAwait(false);
                break;
            case ReconnectCommand reconnect:
                await ReconnectAsync(reconnect, ct).ConfigureAwait(false);
                break;
            case IdleTickCommand tick:
                await IdleTickAsync(tick, ct).ConfigureAwait(false);
                break;
            case ForfeitOnDisconnectCommand forfeit:
                await ForfeitOnDisconnectAsync(forfeit, ct).ConfigureAwait(false);
                break;
            default:
                throw new GameCommandException($"Unsupported command {command.GetType().Name}.");
        }
    }

    private async Task PlayCardAsync(PlayCardCommand command, CancellationToken ct)
    {
        if (!_userIdToSeat.TryGetValue(command.UserId, out int seat))
        {
            await RejectAsync(command.UserId, InvalidMoveCode.NotYourTurn, ct).ConfigureAwait(false);
            return;
        }

        GameState previous = _state;
        try
        {
            _state = _engine.PlayCard(_state, seat, command.Card);
        }
        catch (InvalidMoveException ex)
        {
            await RejectAsync(command.UserId, ex.Code, ct).ConfigureAwait(false);
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        await PersistStateAsync(now, ct).ConfigureAwait(false);
        await _games.AppendMoveAsync(
            _state.GameId,
            NewMove(seat, MoveType.PlayCard, CardPayload(command.Card), now),
            ct).ConfigureAwait(false);

        await _eventBus.PublishAsync(
            new CardPlayedEvent(_state.GameId, now, seat, command.Card),
            ct).ConfigureAwait(false);

        bool trickResolved = previous.CurrentTrick.Length == previous.Hands.Length - 1
            && _state.CurrentTrick.Length == 0;
        if (trickResolved)
        {
            await PublishTrickResolutionAsync(previous, now, ct).ConfigureAwait(false);
        }

        if (previous.Phase != _state.Phase)
        {
            await _eventBus.PublishAsync(
                new PhaseChangedEvent(_state.GameId, now, _state.Phase),
                ct).ConfigureAwait(false);
        }

        if (_state.Phase == GamePhase.Finished && _state.Outcome is not null)
        {
            await SaveFinishedAsync(EndedReason.Normal, now, ct).ConfigureAwait(false);
        }

        await PublishSnapshotsAsync(now, ct).ConfigureAwait(false);
        _lastMoveCompletedAt = now;
        _idleWarnedForSeat = null;
        ScheduleIdleChecks();
    }

    private async Task PublishTrickResolutionAsync(GameState previous, DateTimeOffset now, CancellationToken ct)
    {
        await _eventBus.PublishAsync(
            new TrickResolvedEvent(_state.GameId, now, _state.LeaderSeat, _state.SeatScores),
            ct).ConfigureAwait(false);

        ImmutableArray<int> counts = _state.Hands.Select(static hand => hand.Length).ToImmutableArray();
        foreach ((Guid userId, int seat) in _userIdToSeat)
        {
            Card? drawnCard = FirstNewCard(previous.Hands[seat], _state.Hands[seat]);
            await _eventBus.PublishAsync(
                new CardsDrawnEvent(_state.GameId, now, counts, drawnCard, userId),
                ct).ConfigureAwait(false);
        }
    }

    private async Task ViewOwnPileAsync(ViewOwnPileCommand command, CancellationToken ct)
    {
        if (!_userIdToSeat.TryGetValue(command.UserId, out int seat))
        {
            await RejectAsync(command.UserId, InvalidMoveCode.NotYourTurn, ct).ConfigureAwait(false);
            return;
        }

        if (_state.Phase != GamePhase.LastHand)
        {
            await RejectAsync(command.UserId, InvalidMoveCode.PileViewNotAllowed, ct).ConfigureAwait(false);
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        await _eventBus.PublishAsync(
            new StateUpdatedEvent(
                _state.GameId,
                now,
                SnapshotForUser(command.UserId, _state.Pozzi[seat]),
                command.UserId),
            ct).ConfigureAwait(false);
    }

    private async Task DisconnectAsync(DisconnectCommand command, CancellationToken ct)
    {
        if (!_userIdToSeat.TryGetValue(command.UserId, out int seat))
        {
            return;
        }

        _seatStatuses[seat] = ConnectionStatus.Disconnected;
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset deadline = now.AddSeconds(_options.ReconnectGraceSeconds);
        ReplaceReconnectTimer(
            seat,
            _timers.ScheduleAt(
                deadline,
                async token =>
                {
                    await EnqueueAsync(
                        new ForfeitOnDisconnectCommand(_state.GameId, seat),
                        token).ConfigureAwait(false);
                }));

        await _games.AppendMoveAsync(
            _state.GameId,
            NewMove(seat, MoveType.Disconnect, "{}", now),
            ct).ConfigureAwait(false);
        await _eventBus.PublishAsync(
            new PlayerDisconnectedEvent(_state.GameId, now, seat, deadline),
            ct).ConfigureAwait(false);
    }

    private async Task ReconnectAsync(ReconnectCommand command, CancellationToken ct)
    {
        if (!_userIdToSeat.TryGetValue(command.UserId, out int seat))
        {
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        bool wasDisconnected = _seatStatuses.GetValueOrDefault(seat) == ConnectionStatus.Disconnected;
        _seatStatuses[seat] = ConnectionStatus.Connected;
        CancelReconnectTimer(seat);

        await _games.AppendMoveAsync(
            _state.GameId,
            NewMove(seat, MoveType.Reconnect, "{}", now),
            ct).ConfigureAwait(false);

        if (wasDisconnected)
        {
            await _eventBus.PublishAsync(
                new PlayerReconnectedEvent(_state.GameId, now, seat),
                ct).ConfigureAwait(false);
        }

        await _eventBus.PublishAsync(
            new JoinedEvent(_state.GameId, now, SnapshotForUser(command.UserId), command.UserId),
            ct).ConfigureAwait(false);
    }

    private async Task IdleTickAsync(IdleTickCommand command, CancellationToken ct)
    {
        if (_state.Phase == GamePhase.Finished)
        {
            return;
        }

        int idleSeat = _state.NextToPlaySeat;
        DateTimeOffset warnAt = _lastMoveCompletedAt.AddSeconds(_options.IdleWarnSeconds);
        DateTimeOffset forfeitAt = _lastMoveCompletedAt.AddSeconds(_options.IdleForfeitSeconds);

        if (command.At >= forfeitAt)
        {
            await ForfeitAsync(idleSeat, EndedReason.ForfeitIdle, MoveType.IdleTimeout, command.At, ct)
                .ConfigureAwait(false);
            return;
        }

        if (command.At >= warnAt && _idleWarnedForSeat != idleSeat)
        {
            _idleWarnedForSeat = idleSeat;
            await _eventBus.PublishAsync(
                new IdleWarningEvent(_state.GameId, command.At, idleSeat, forfeitAt),
                ct).ConfigureAwait(false);
        }
    }

    private async Task ForfeitOnDisconnectAsync(ForfeitOnDisconnectCommand command, CancellationToken ct)
    {
        if (_state.Phase == GamePhase.Finished)
        {
            return;
        }

        if (_seatStatuses.GetValueOrDefault(command.SeatIndex) != ConnectionStatus.Disconnected)
        {
            return;
        }

        await ForfeitAsync(
            command.SeatIndex,
            EndedReason.ForfeitDisconnect,
            MoveType.Forfeit,
            _clock.UtcNow,
            ct).ConfigureAwait(false);
    }

    private async Task ForfeitAsync(
        int forfeitingSeat,
        EndedReason reason,
        MoveType moveType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        GameOutcome outcome = new GameOutcome.Winner(WinnerKeyAgainst(forfeitingSeat));
        _state = _state with
        {
            Phase = GamePhase.Finished,
            Outcome = outcome,
        };

        await PersistStateAsync(now, ct).ConfigureAwait(false);
        await _games.AppendMoveAsync(
            _state.GameId,
            NewMove(forfeitingSeat, moveType, "{}", now),
            ct).ConfigureAwait(false);
        await SaveFinishedAsync(reason, now, ct).ConfigureAwait(false);
        await PublishSnapshotsAsync(now, ct).ConfigureAwait(false);
    }

    private async Task SaveFinishedAsync(EndedReason reason, DateTimeOffset now, CancellationToken ct)
    {
        if (_state.Outcome is null)
        {
            return;
        }

        await _games.SaveResultAsync(ToResultRecord(_state, reason), ct).ConfigureAwait(false);
        await _eventBus.PublishAsync(
            new GameFinishedEvent(_state.GameId, now, _state.Outcome, _state.SeatScores, reason),
            ct).ConfigureAwait(false);
    }

    private async Task PersistStateAsync(DateTimeOffset now, CancellationToken ct)
    {
        GameRecord desired = _record with
        {
            Status = _state.Phase == GamePhase.Finished ? GameStatus.Finished : GameStatus.Running,
            EndedAt = _state.Phase == GamePhase.Finished ? now : _record.EndedAt,
            StateSnapshotJson = _stateCodec.Serialize(_state),
            BriscolaSuit = _state.BriscolaSuit,
            ShuffleSeed = _state.ShuffleSeed,
        };

        bool updated = await _games.UpdateAsync(desired, ct).ConfigureAwait(false);
        if (!updated)
        {
            throw new GameCommandException($"Game {desired.Id} could not be updated due to concurrency.");
        }

        _record = desired with { Version = desired.Version + 1 };
    }

    private async Task PublishSnapshotsAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (Guid userId in _userIdToSeat.Keys)
        {
            await _eventBus.PublishAsync(
                new StateUpdatedEvent(_state.GameId, now, SnapshotForUser(userId), userId),
                ct).ConfigureAwait(false);
        }
    }

    private RedactedStateForUser SnapshotForUser(Guid userId, ImmutableArray<Card>? myPozzo = null)
    {
        ImmutableArray<Card>? myHand = null;
        if (_userIdToSeat.TryGetValue(userId, out int seat))
        {
            myHand = _state.Hands[seat];
        }

        return new RedactedStateForUser(
            _state.GameId,
            _state.Mode,
            _state.Phase,
            _state.DealerSeat,
            _state.LeaderSeat,
            _state.NextToPlaySeat,
            _state.TrickNumber,
            _state.BriscolaCard,
            _state.BriscolaSuit,
            _state.Stock.Length,
            _state.Hands.Select(static hand => hand.Length).ToImmutableArray(),
            myHand,
            myPozzo,
            _state.CurrentTrick,
            _state.SeatScores,
            _state.Outcome);
    }

    private async Task RejectAsync(Guid targetUserId, InvalidMoveCode code, CancellationToken ct)
    {
        await _eventBus.PublishAsync(
            new InvalidMoveRejectedEvent(_state.GameId, _clock.UtcNow, targetUserId, code),
            ct).ConfigureAwait(false);
    }

    private void ScheduleIdleChecks()
    {
        foreach (IDisposable timer in _idleTimers)
        {
            timer.Dispose();
        }

        _idleTimers.Clear();
        if (_state.Phase == GamePhase.Finished)
        {
            return;
        }

        DateTimeOffset warnAt = _lastMoveCompletedAt.AddSeconds(_options.IdleWarnSeconds);
        DateTimeOffset forfeitAt = _lastMoveCompletedAt.AddSeconds(_options.IdleForfeitSeconds);
        _idleTimers.Add(ScheduleIdleCommand(warnAt));
        _idleTimers.Add(ScheduleIdleCommand(forfeitAt));
    }

    private IDisposable ScheduleIdleCommand(DateTimeOffset at) =>
        _timers.ScheduleAt(
            at,
            async token =>
            {
                await EnqueueAsync(new IdleTickCommand(_state.GameId, at), token).ConfigureAwait(false);
            });

    private void ReplaceReconnectTimer(int seat, IDisposable timer)
    {
        CancelReconnectTimer(seat);
        _reconnectTimers[seat] = timer;
    }

    private void CancelReconnectTimer(int seat)
    {
        if (_reconnectTimers.Remove(seat, out IDisposable? timer))
        {
            timer.Dispose();
        }
    }

    private int WinnerKeyAgainst(int forfeitingSeat)
    {
        if (_state.Mode == GameMode.TwoPlayer)
        {
            return forfeitingSeat == 0 ? 1 : 0;
        }

        return forfeitingSeat % 2 == 0 ? 1 : 0;
    }

    private MoveRecord NewMove(int seat, MoveType type, string payload, DateTimeOffset now) =>
        new(Guid.NewGuid(), _state.GameId, _moveIndex++, seat, type, payload, now);

    private static string CardPayload(Card card) => $$"""{"suit":"{{card.Suit}}","rank":"{{card.Rank}}"}""";

    private static Card? FirstNewCard(ImmutableArray<Card> before, ImmutableArray<Card> after)
    {
        List<Card> remaining = [.. before];
        foreach (Card card in after)
        {
            if (!remaining.Remove(card))
            {
                return card;
            }
        }

        return null;
    }

    private static GameResultRecord ToResultRecord(GameState state, EndedReason reason)
    {
        return state.Outcome switch
        {
            GameOutcome.Winner winner => new GameResultRecord(
                state.GameId,
                GameOutcomeKind.Win,
                winner.SeatOrTeam,
                ScoresText(state.SeatScores),
                TeamScoresText(state),
                reason),
            GameOutcome.Draw => new GameResultRecord(
                state.GameId,
                GameOutcomeKind.Draw,
                null,
                ScoresText(state.SeatScores),
                TeamScoresText(state),
                reason),
            _ => throw new InvalidOperationException("A finished result requires an outcome."),
        };
    }

    private static string ScoresText(ImmutableArray<int> scores) => $"[{string.Join(",", scores)}]";

    private static string? TeamScoresText(GameState state)
    {
        if (state.Mode != GameMode.FourPlayerTeams)
        {
            return null;
        }

        int teamZero = state.SeatScores[0] + state.SeatScores[2];
        int teamOne = state.SeatScores[1] + state.SeatScores[3];
        return $"[{teamZero},{teamOne}]";
    }

    private sealed record QueuedCommand(GameCommand Command, TaskCompletionSource Completion);
}
