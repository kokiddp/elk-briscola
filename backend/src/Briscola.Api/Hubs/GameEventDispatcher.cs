using System.Collections.Immutable;
using Briscola.Api.Dtos;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
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
    private readonly IHubContext<LobbyHub, ILobbyClient> _lobby;
    private readonly ILogger<GameEventDispatcher> _logger;
    private readonly Briscola.Application.Telemetry.BriscolaMetrics _metrics;
    private readonly Briscola.Application.Orchestration.GameOrchestrator _orchestrator;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopes;

    // Per-game cache of the positional [seat → display-name + Elo] map.
    // Populated lazily on the first JoinedEvent / StateUpdatedEvent for a
    // game (one DB round-trip via IPlayerDirectory) and refreshed on
    // RankingUpdatedEvent. Evicted when the game finishes.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ImmutableArray<PlayerInfoDto?>> _seatPlayersByGame = new();

    public GameEventDispatcher(
        IGameEventBus bus,
        IHubContext<GameHub, IGameClient> hub,
        IHubContext<LobbyHub, ILobbyClient> lobby,
        ILogger<GameEventDispatcher> logger,
        Briscola.Application.Telemetry.BriscolaMetrics metrics,
        Briscola.Application.Orchestration.GameOrchestrator orchestrator,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
    {
        _bus = bus;
        _hub = hub;
        _lobby = lobby;
        _logger = logger;
        _metrics = metrics;
        _orchestrator = orchestrator;
        _scopes = scopes;
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
        JoinedEvent j => DispatchJoinedAsync(j),
        StateUpdatedEvent s => DispatchStateUpdatedAsync(s),
        CardPlayedEvent c => DispatchCardPlayedAsync(c),
        TrickResolvedEvent t =>
            BroadcastGroups(t.GameId).TrickResolved(new TrickResolvedDto(t.WinnerSeat, t.NewSeatScores)),
        CardsDrawnEvent d => DispatchCardsDrawnAsync(d),
        PhaseChangedEvent p =>
            BroadcastGroups(p.GameId).PhaseChanged(p.NewPhase.ToString()),
        GameFinishedEvent f => DispatchGameFinishedAsync(f),
        RankingUpdatedEvent ru => DispatchRankingUpdatedAsync(ru),
        PlayerDisconnectedEvent pd =>
            BroadcastGroups(pd.GameId).PlayerDisconnected(pd.SeatIndex, pd.GraceDeadlineUtc),
        PlayerReconnectedEvent pr =>
            BroadcastGroups(pr.GameId).PlayerReconnected(pr.SeatIndex),
        IdleWarningEvent iw =>
            BroadcastGroups(iw.GameId).IdleWarning(iw.SeatIndex, iw.ForfeitDeadlineUtc),
        InvalidMoveRejectedEvent im =>
            _hub.Clients.User(im.TargetUserId.ToString()).InvalidMove(im.Code.ToString()),
        ChatMessageEvent => Task.CompletedTask, // chat is broadcast directly by GameHub.SendChat
        LobbyGameCreatedEvent lc =>
            LobbyGroup().GameCreated(ToSummaryDto(lc.Summary)),
        // Seat-change on an open game invalidates any cached seat-
        // players snapshot. We don't proactively rebuild — just evict;
        // the next Joined / StateUpdated for the game will re-resolve.
        // Audit M4.
        LobbyGameUpdatedEvent lu =>
            DispatchLobbyUpdatedAsync(lu),
        LobbyGameStartedEvent ls =>
            LobbyGroup().GameStarted(ls.GameId),
        LobbyGameEndedEvent le =>
            DispatchLobbyEndedAsync(le),
        _ => Task.CompletedTask,
    };

    private ILobbyClient LobbyGroup() => _lobby.Clients.Group(LobbyHub.OpenLobbyGroup);

    private Task DispatchLobbyUpdatedAsync(LobbyGameUpdatedEvent evt)
    {
        _seatPlayersByGame.TryRemove(evt.GameId, out _);
        return LobbyGroup().GameUpdated(ToSummaryDto(evt.Summary));
    }

    private Task DispatchLobbyEndedAsync(LobbyGameEndedEvent evt)
    {
        // Belt-and-suspenders eviction. GameFinished already evicts via
        // DispatchGameFinishedAsync, but the open-lobby janitor's path
        // (a lone-leaver abandon) only emits LobbyGameEnded — without
        // this the cache would stick around for an already-dead game.
        _seatPlayersByGame.TryRemove(evt.GameId, out _);
        return LobbyGroup().GameEnded(evt.GameId);
    }

    private async Task DispatchJoinedAsync(JoinedEvent evt)
    {
        ImmutableArray<PlayerInfoDto?> seatPlayers = await ResolveSeatPlayersAsync(evt.GameId).ConfigureAwait(false);
        await _hub.Clients
            .User(evt.TargetUserId.ToString())
            .Joined(ToDto(evt.Snapshot, seatPlayers))
            .ConfigureAwait(false);
    }

    private async Task DispatchStateUpdatedAsync(StateUpdatedEvent evt)
    {
        ImmutableArray<PlayerInfoDto?> seatPlayers = await ResolveSeatPlayersAsync(evt.GameId).ConfigureAwait(false);
        RedactedStateForUserDto dto = ToDto(evt.Snapshot, seatPlayers);
        if (evt.TargetUserId is null)
        {
            await _hub.Clients.Group(GameHub.SpectatorGroup(evt.GameId))
                .StateUpdated(dto).ConfigureAwait(false);
        }
        else
        {
            await _hub.Clients.User(evt.TargetUserId.Value.ToString())
                .StateUpdated(dto).ConfigureAwait(false);
        }
    }

    private Task DispatchCardPlayedAsync(CardPlayedEvent evt)
    {
        _metrics.MovesTotal.Add(1);
        return BroadcastGroups(evt.GameId)
            .CardPlayed(new CardPlayedDto(evt.SeatIndex, ToDto(evt.Card)));
    }

    /// <summary>
    /// Lazily resolves + caches the per-game seat-players list. First call
    /// for a game opens a scope, loads the GameRecord + display names + Elos
    /// via IPlayerDirectory. Subsequent calls return the cached array.
    /// </summary>
    private async Task<ImmutableArray<PlayerInfoDto?>> ResolveSeatPlayersAsync(Guid gameId)
    {
        if (_seatPlayersByGame.TryGetValue(gameId, out ImmutableArray<PlayerInfoDto?> cached))
        {
            return cached;
        }

        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _scopes.CreateScope();
        IGameRepository repo = scope.ServiceProvider
            .GetRequiredService<IGameRepository>();
        IPlayerDirectory dir = scope.ServiceProvider
            .GetRequiredService<IPlayerDirectory>();
        GameRecord? record = await repo.GetAsync(gameId, CancellationToken.None).ConfigureAwait(false);
        if (record is null)
        {
            return ImmutableArray<PlayerInfoDto?>.Empty;
        }
        IReadOnlyDictionary<Guid, PlayerInfo> map =
            await dir.GetAsync(record.SeatUserIds.OfType<Guid>(), CancellationToken.None)
                .ConfigureAwait(false);
        ImmutableArray<PlayerInfoDto?> built = record.SeatUserIds
            .Select(id => id is { } uid && map.TryGetValue(uid, out PlayerInfo? p)
                ? new PlayerInfoDto(p.UserId, p.DisplayName, p.Elo)
                : null)
            .ToImmutableArray();
        return _seatPlayersByGame.GetOrAdd(gameId, built);
    }

    private async Task DispatchGameFinishedAsync(GameFinishedEvent evt)
    {
        await BroadcastGroups(evt.GameId)
            .GameFinished(new GameFinishedDto(ToDto(evt.Outcome), evt.SeatScores, evt.Reason.ToString()))
            .ConfigureAwait(false);
        await LobbyGroup().GameEnded(evt.GameId).ConfigureAwait(false);

        // Free the room from the in-memory orchestrator dict + the
        // seat-players cache. Without this, every finished game stayed in
        // _rooms until process exit, leaking memory + skewing
        // briscola.active_games into "loaded rooms" rather than
        // "actually-running games".
        _seatPlayersByGame.TryRemove(evt.GameId, out _);
        await _orchestrator.DisposeRoomAsync(evt.GameId).ConfigureAwait(false);
    }

    private Task DispatchRankingUpdatedAsync(RankingUpdatedEvent evt)
    {
        // Patch the per-seat Elo in the cache so the next snapshot for
        // this game reflects the freshly-updated ranking on the player
        // whose entry just changed.
        _seatPlayersByGame.AddOrUpdate(
            evt.GameId,
            // No cache yet — leave it empty; next snapshot will populate.
            _ => ImmutableArray<PlayerInfoDto?>.Empty,
            (_, existing) => existing.Length == 0
                ? existing
                : existing
                    .Select(p => p is not null && p.UserId == evt.TargetUserId
                        ? new PlayerInfoDto(p.UserId, p.DisplayName, evt.Ranking.Elo)
                        : p)
                    .ToImmutableArray());

        return _hub.Clients.User(evt.TargetUserId.ToString())
            .RankingUpdated(new RankingDto(
                evt.Ranking.Elo,
                evt.Ranking.Wins,
                evt.Ranking.Losses,
                evt.Ranking.Draws,
                evt.Ranking.GamesPlayed,
                evt.Ranking.UpdatedAt));
    }

    private static GameSummaryDto ToSummaryDto(GameSummary s) =>
        new(
            s.Id,
            s.Mode,
            s.Name,
            s.Status,
            s.OccupiedSeats,
            s.TotalSeats,
            s.IsPrivate,
            s.CreatedAt,
            s.StartedAt,
            s.SeatPlayers
                .Select(p => p is null ? null : new PlayerInfoDto(p.UserId, p.DisplayName, p.Elo))
                .ToImmutableArray());

    /// <summary>
    /// Returns a client proxy that fans broadcasts to both the players'
    /// group <c>game:{id}</c> and the spectators' group
    /// <c>game:{id}:spectators</c>. SignalR happily accepts an absent
    /// group name, so spectator-less games still work.
    /// </summary>
    private IGameClient BroadcastGroups(Guid gameId) =>
        _hub.Clients.Groups(GameHub.GameGroup(gameId), GameHub.SpectatorGroup(gameId));

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

    /// <summary>
    /// Wire-DTO conversion for an application-layer redacted snapshot.
    /// Exposed so <see cref="GameHub.SpectateGame"/> can produce the
    /// initial-state DTO without duplicating the mapping. Kept internal
    /// — only the hub bridge consumes it.
    /// </summary>
    /// <summary>
    /// Called by GameHub.SpectateGame to deliver the initial-state DTO
    /// without going through the dispatcher's per-snapshot enrichment.
    /// Spectators don't need seat-players right at the first frame —
    /// the next StateUpdated will fill them in.
    /// </summary>
    internal static RedactedStateForUserDto ToWireDto(RedactedStateForUser snapshot) =>
        ToDto(snapshot, ImmutableArray<PlayerInfoDto?>.Empty);

    private static RedactedStateForUserDto ToDto(
        RedactedStateForUser snapshot,
        ImmutableArray<PlayerInfoDto?> seatPlayers) =>
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
            snapshot.Outcome is null ? null : ToDto(snapshot.Outcome),
            snapshot.MySeatIndex,
            seatPlayers,
            snapshot.ActiveSeatForfeitDeadline);
}
