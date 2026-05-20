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

public sealed class GameRoom : IAsyncDisposable
{
    private readonly IGameRepositoryFactory _gamesFactory;
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

    /// <summary>
    /// Pending idle-tick disposables (warn + forfeit). Mutated *only*
    /// from <see cref="ProcessLoopAsync"/> — every Schedule/Cancel call
    /// site is reached via <see cref="ApplyAsync"/>. Timer fire-callbacks
    /// don't touch this list; they re-enter via <see cref="EnqueueAsync"/>
    /// and the next loop iteration handles cleanup. Do not call
    /// <see cref="ScheduleIdleChecks"/> / <see cref="CancelIdleTimers"/>
    /// from any thread other than the loop or this invariant breaks.
    /// </summary>
    private readonly List<IDisposable> _idleTimers = [];
    private readonly Task _processLoop;

    private GameRecord _record;
    private GameState _state;
    private int _moveIndex;
    private DateTimeOffset _lastMoveCompletedAt;
    private int? _idleWarnedForSeat;
    private bool _disposed;

    private GameRoom(
        GameRecord record,
        GameState state,
        IGameRepositoryFactory gamesFactory,
        IGameStateCodec stateCodec,
        IBriscolaEngine engine,
        IGameEventBus eventBus,
        IClock clock,
        ITimerService timers,
        IOptions<GameOptions> options)
    {
        _record = record;
        _state = state;
        _gamesFactory = gamesFactory;
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
        _processLoop = Task.Run(ProcessLoopAsync);
    }

    public GameState CurrentState => _state;

    public static GameRoom FromRecord(
        GameRecord record,
        IGameRepositoryFactory gamesFactory,
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
        return new GameRoom(record, state, gamesFactory, stateCodec, engine, eventBus, clock, timers, options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _commands.Writer.TryComplete();
        CancelIdleTimers();
        foreach (IDisposable timer in _reconnectTimers.Values)
        {
            timer.Dispose();
        }

        _reconnectTimers.Clear();

        try
        {
            await _processLoop.ConfigureAwait(false);
        }
        catch
        {
            // The loop swallows individual command exceptions into the
            // queued completion sources; reaching here means the channel
            // closed cleanly. Any straggler exception is harmless.
        }
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
        // Hydrate the move-log counter from the repository before any commands
        // are applied. Without this, restarting the process and rehydrating an
        // in-flight game would cause MoveIndex to restart at 0 and collide with
        // the unique (GameId, MoveIndex) index documented in the README schema.
        try
        {
            await using IGameRepositoryScope hydrateScope = _gamesFactory.Create();
            _moveIndex = await hydrateScope.Repository
                .GetNextMoveIndexAsync(_state.GameId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // If hydrate fails (DB unavailable at room construction) the
            // loop used to just throw out of ProcessLoopAsync — the task
            // faulted, but any EnqueueAsync was already blocked on
            // completion.Task, so the producer would hang forever.
            // Drain everything currently in the channel + every later
            // arrival with the same hydrate failure so callers fail fast.
            // Audit L9.
            _commands.Writer.TryComplete(ex);
            while (_commands.Reader.TryRead(out QueuedCommand pending))
            {
                pending.Completion.TrySetException(ex);
            }
            return;
        }

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
        await PersistStateAndAppendMoveAsync(
            NewMove(seat, MoveType.PlayCard, CardPayload(command.Card), now),
            now,
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

        // Stamp the turn-start instant BEFORE publishing the snapshot, so
        // RedactedStateForUser.ActiveSeatForfeitDeadline reflects the new
        // turn's window (now + IdleForfeitSeconds) rather than the previous
        // turn's stale deadline. Without this the per-turn countdown chip
        // never resets between moves.
        _lastMoveCompletedAt = now;
        await PublishSnapshotsAsync(now, ct).ConfigureAwait(false);
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
        CancelIdleTimers();
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

        await AppendMoveAsync(
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

        await AppendMoveAsync(
            NewMove(seat, MoveType.Reconnect, "{}", now),
            ct).ConfigureAwait(false);

        if (wasDisconnected)
        {
            await _eventBus.PublishAsync(
                new PlayerReconnectedEvent(_state.GameId, now, seat),
                ct).ConfigureAwait(false);
            ScheduleIdleChecks();
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

        await PersistStateAndAppendMoveAsync(
            NewMove(forfeitingSeat, moveType, "{}", now),
            now,
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

        GameResultRecord resultRecord = ToResultRecord(_state, reason);
        IReadOnlyList<Persistence.RankingRecord> updatedRankings;
        await using (IGameRepositoryScope scope = _gamesFactory.Create())
        {
            await scope.Repository.SaveResultAsync(resultRecord, ct).ConfigureAwait(false);
            // Apply ranking inside the same scope so RankingService shares
            // the EF DbContext / unit-of-work with the result persistence.
            // ApplyResultAsync is idempotent on result.GameId, so retries
            // after a partial failure don't double-count.
            updatedRankings = await scope.Ranking
                .ApplyResultAsync(_record, resultRecord, ct).ConfigureAwait(false);
        }

        // Fan out the per-player ranking updates BEFORE the GameFinished
        // event so the SPA's cached /me snapshot is patched while the
        // hub connection still exists (the end-game dialog typically
        // navigates the user away shortly after GameFinished lands).
        foreach (Persistence.RankingRecord ranking in updatedRankings)
        {
            await _eventBus.PublishAsync(
                new RankingUpdatedEvent(_state.GameId, now, ranking.UserId, ranking),
                ct).ConfigureAwait(false);
        }

        await _eventBus.PublishAsync(
            new GameFinishedEvent(_state.GameId, now, _state.Outcome, _state.SeatScores, reason),
            ct).ConfigureAwait(false);
    }

    private async Task AppendMoveAsync(MoveRecord move, CancellationToken ct)
    {
        await using IGameRepositoryScope scope = _gamesFactory.Create();
        await scope.Repository.AppendMoveAsync(_state.GameId, move, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomic snapshot-bump + move-log append: both rows commit in one
    /// transaction so a crash between them can't leave the persisted
    /// snapshot ahead of the move log (replay = ShuffleSeed + GameMoves,
    /// see ADR 0005). Throws <see cref="GameCommandException"/> on
    /// optimistic-concurrency loss, same as the old PersistStateAsync.
    /// </summary>
    private async Task PersistStateAndAppendMoveAsync(MoveRecord move, DateTimeOffset now, CancellationToken ct)
    {
        GameRecord desired = _record with
        {
            Status = _state.Phase == GamePhase.Finished ? GameStatus.Finished : GameStatus.Running,
            EndedAt = _state.Phase == GamePhase.Finished ? now : _record.EndedAt,
            StateSnapshotJson = _stateCodec.Serialize(_state),
            BriscolaSuit = _state.BriscolaSuit,
            ShuffleSeed = _state.ShuffleSeed,
        };

        bool updated;
        await using (IGameRepositoryScope scope = _gamesFactory.Create())
        {
            updated = await scope.Repository
                .UpdateAndAppendMoveAsync(desired, move, ct)
                .ConfigureAwait(false);
        }

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

        // Spectator variant: same snapshot shape, no per-user hand info.
        // The dispatcher fans this onto the game:{id}:spectators group.
        await _eventBus.PublishAsync(
            new StateUpdatedEvent(_state.GameId, now, SpectatorSnapshot(), TargetUserId: null),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Spectator-redacted snapshot of the current state: counts only,
    /// no <c>MyHand</c> / <c>MyPozzo</c>. Safe to call from any thread;
    /// reads <c>_state</c> which is mutated only on the room's command
    /// loop (concurrent reads see a consistent <c>GameState</c> record
    /// thanks to its immutability).
    /// </summary>
    public RedactedStateForUser SpectatorSnapshot() => SnapshotForUser(Guid.Empty);

    private RedactedStateForUser SnapshotForUser(Guid userId, ImmutableArray<Card>? myPozzo = null)
    {
        ImmutableArray<Card>? myHand = null;
        int? mySeatIndex = null;
        if (_userIdToSeat.TryGetValue(userId, out int seat))
        {
            myHand = _state.Hands[seat];
            mySeatIndex = seat;
        }

        // The active seat's forfeit deadline is reset every move via
        // _lastMoveCompletedAt; surfacing it here lets every client run
        // a per-turn countdown without having to know IdleForfeitSeconds
        // out-of-band. Null outside the actively-playing phases.
        DateTimeOffset? activeSeatForfeitDeadline =
            _state.Phase is GamePhase.Playing or GamePhase.LastHand
                ? _lastMoveCompletedAt.AddSeconds(_options.IdleForfeitSeconds)
                : null;

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
            _state.Outcome,
            mySeatIndex,
            activeSeatForfeitDeadline);
    }

    private async Task RejectAsync(Guid targetUserId, InvalidMoveCode code, CancellationToken ct)
    {
        await _eventBus.PublishAsync(
            new InvalidMoveRejectedEvent(_state.GameId, _clock.UtcNow, targetUserId, code),
            ct).ConfigureAwait(false);
    }

    private void ScheduleIdleChecks()
    {
        CancelIdleTimers();
        if (_state.Phase == GamePhase.Finished)
        {
            return;
        }

        DateTimeOffset warnAt = _lastMoveCompletedAt.AddSeconds(_options.IdleWarnSeconds);
        DateTimeOffset forfeitAt = _lastMoveCompletedAt.AddSeconds(_options.IdleForfeitSeconds);
        _idleTimers.Add(ScheduleIdleCommand(warnAt));
        _idleTimers.Add(ScheduleIdleCommand(forfeitAt));
    }

    private void CancelIdleTimers()
    {
        foreach (IDisposable timer in _idleTimers)
        {
            timer.Dispose();
        }

        _idleTimers.Clear();
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
        // 2p: the surviving seat (0 or 1) is the winner.
        if (_state.Mode == GameMode.TwoPlayer)
        {
            return forfeitingSeat == 0 ? 1 : 0;
        }

        // 4p teams: team 0 = seats {0, 2}; team 1 = seats {1, 3}. The
        // forfeiting seat's team loses, so the winner key is the opposite team.
        return forfeitingSeat % 2 == 0 ? 1 : 0;
    }

    // _moveIndex is mutated only on the room's single ProcessLoopAsync task
    // (see EnqueueAsync), so non-atomic increment is safe.
    private MoveRecord NewMove(int seat, MoveType type, string payload, DateTimeOffset now) =>
        new(Guid.NewGuid(), _state.GameId, _moveIndex++, seat, type, payload, now);

    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions =
        new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            WriteIndented = false,
        };

    private static string CardPayload(Card card) =>
        System.Text.Json.JsonSerializer.Serialize(
            new { suit = card.Suit, rank = card.Rank },
            PayloadJsonOptions);

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
