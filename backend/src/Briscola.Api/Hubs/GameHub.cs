using System.Collections.Concurrent;
using Briscola.Api.Configuration;
using Briscola.Api.Dtos;
using Briscola.Api.Hubs.Limits;
using Briscola.Application.Errors;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Briscola.Api.Hubs;

/// <summary>
/// Real-time channel for one in-progress game. Connections that have
/// joined a game are members of the <c>game:{gameId}</c> SignalR group;
/// per-recipient events go to that connection's <c>UserIdentifier</c>;
/// spectators (Phase 5.5) join <c>game:{gameId}:spectators</c>.
///
/// Hub-method handlers translate authenticated client invocations into
/// <see cref="GameCommand"/>s on the orchestrator's per-game channel.
/// They never compute game state — that's the room's job. Server-to-
/// client broadcast for game events (CardPlayed / TrickResolved / etc.)
/// is the <see cref="GameEventDispatcher"/>'s job (Phase 5.2b); this
/// hub only owns group membership, command enqueue, and the chat
/// round-trip.
/// </summary>
[Authorize]
public sealed class GameHub : Hub<IGameClient>
{
    public const string GameGroupPrefix = "game:";
    public const string SpectatorGroupSuffix = ":spectators";
    private const string JoinedGamesItemKey = "JoinedGames";
    private const string SpectatedGamesItemKey = "SpectatedGames";
    private const int MaxChatTextLength = 500;

    // Rate-limit policies. Defaults from TODO Phase 5.6:
    // - PlayCard: 1 per second (excess → InvalidMove("RateLimited"))
    // - SendChat: 5 per 10 seconds (excess → same)
    // Bound to HubRateLimitOptions so deployments and the integration
    // suite can dial them up or down. Setting either Window to 0
    // disables the limit (any call admits).
    private readonly HubRateLimitOptions _rateLimits;

    private readonly GameOrchestrator _orchestrator;
    private readonly LobbyService _lobby;
    private readonly IGameRepository _games;
    private readonly IChatRepository _chat;
    private readonly IClock _clock;
    private readonly Briscola.Application.Telemetry.BriscolaMetrics _metrics;
    private readonly Briscola.Api.Hubs.Limits.HubMethodRateLimiter _rateLimiter;

    public GameHub(
        GameOrchestrator orchestrator,
        LobbyService lobby,
        IGameRepository games,
        IChatRepository chat,
        IClock clock,
        IOptions<HubRateLimitOptions> rateLimits,
        Briscola.Application.Telemetry.BriscolaMetrics metrics,
        Briscola.Api.Hubs.Limits.HubMethodRateLimiter rateLimiter)
    {
        ArgumentNullException.ThrowIfNull(rateLimits);
        _orchestrator = orchestrator;
        _lobby = lobby;
        _games = games;
        _chat = chat;
        _clock = clock;
        _rateLimits = rateLimits.Value;
        _metrics = metrics;
        _rateLimiter = rateLimiter;
    }

    public override async Task OnConnectedAsync()
    {
        _metrics.ConnectedPlayers.Add(1);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public static string GameGroup(Guid gameId) => $"{GameGroupPrefix}{gameId}";

    public static string SpectatorGroup(Guid gameId) =>
        $"{GameGroupPrefix}{gameId}{SpectatorGroupSuffix}";

    /// <summary>
    /// Joins the <c>game:{gameId}</c> group and enqueues a
    /// <see cref="ReconnectCommand"/> so the room emits a fresh
    /// <c>StateUpdated</c>/<c>Joined</c> snapshot back to the caller.
    /// </summary>
    public async Task JoinGame(Guid gameId)
    {
        Guid userId = ResolveUserId();
        GameRecord record = await EnsureUserIsParticipantAsync(gameId, userId).ConfigureAwait(false);

        // The room only exists for Running games. If the caller deep-links
        // to /game/:id while the game is still Open (e.g. they refresh the
        // creator's tab before opponents fill the seats), bounce out with
        // a stable error code instead of crashing the room constructor.
        if (record.Status != GameStatus.Running)
        {
            await Clients.Caller.InvalidMove("WrongPhase").ConfigureAwait(false);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(gameId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        TrackJoinedGame(gameId);

        await _orchestrator
            .EnqueueAsync(new ReconnectCommand(gameId, userId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task PlayCard(Guid gameId, CardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Guid userId = ResolveUserId();

        if (!TryAcquireRateLimit(
            nameof(PlayCard), _rateLimits.PlayCardPermits, _rateLimits.PlayCardWindowSeconds))
        {
            await Clients.Caller.InvalidMove("RateLimited").ConfigureAwait(false);
            return;
        }

        await _orchestrator
            .EnqueueAsync(
                new PlayCardCommand(gameId, userId, new Card(card.Suit, card.Rank)),
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task ViewOwnPile(Guid gameId)
    {
        Guid userId = ResolveUserId();
        await _orchestrator
            .EnqueueAsync(new ViewOwnPileCommand(gameId, userId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Joins the <c>game:{gameId}:spectators</c> group and sends a
    /// spectator-redacted snapshot back to the caller. Participants
    /// should call <see cref="JoinGame"/> instead — they get the
    /// per-recipient snapshot with their hand baked in.
    /// </summary>
    public async Task SpectateGame(Guid gameId)
    {
        Guid userId = ResolveUserId();
        GameRecord? record = await _games.GetAsync(gameId, Context.ConnectionAborted).ConfigureAwait(false)
            ?? throw new HubException($"Game {gameId} not found.");
        if (record.Status != GameStatus.Running)
        {
            throw new HubException("Only running games can be spectated.");
        }

        if (record.SeatUserIds.Contains(userId))
        {
            throw new HubException("Participants must call JoinGame, not SpectateGame.");
        }

        if (!_orchestrator.TryGetRoom(gameId, out GameRoom? room) || room is null)
        {
            throw new HubException($"Game {gameId} is not active in memory.");
        }

        await Groups
            .AddToGroupAsync(Context.ConnectionId, SpectatorGroup(gameId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        TrackSpectatedGame(gameId);

        Briscola.Application.Orchestration.Events.RedactedStateForUser snapshot = room.SpectatorSnapshot();
        await Clients.Caller.StateUpdated(GameEventDispatcher.ToWireDto(snapshot)).ConfigureAwait(false);
    }

    public async Task UnspectateGame(Guid gameId)
    {
        await Groups
            .RemoveFromGroupAsync(Context.ConnectionId, SpectatorGroup(gameId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        UntrackSpectatedGame(gameId);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Open game → delegates to <see cref="LobbyService.LeaveAsync"/>
    /// (the lobby cares about open seats). Running game → enqueues a
    /// <see cref="DisconnectCommand"/>; the room starts the reconnect
    /// grace timer. Either way, the connection drops the
    /// <c>game:{gameId}</c> group on success.
    /// </summary>
    public async Task LeaveGame(Guid gameId)
    {
        Guid userId = ResolveUserId();
        GameRecord? record = await _games.GetAsync(gameId, Context.ConnectionAborted).ConfigureAwait(false);
        if (record is null)
        {
            throw new HubException($"Game {gameId} not found.");
        }

        if (record.Status == GameStatus.Open)
        {
            await _lobby.LeaveAsync(gameId, userId, Context.ConnectionAborted).ConfigureAwait(false);
        }
        else
        {
            await _orchestrator
                .EnqueueAsync(new DisconnectCommand(gameId, userId), Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GameGroup(gameId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        UntrackJoinedGame(gameId);
    }

    public async Task SendChat(Guid gameId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Guid userId = ResolveUserId();

        // Phase 5.5 spectator policy: connections that joined the
        // spectator group but never the players' group cannot chat.
        // Reject with a targeted InvalidMove ("SpectatorsCannotChat")
        // rather than throwing — the spec is explicit about the no-op
        // + targeted error shape.
        if (IsSpectatorOnly(gameId))
        {
            await Clients.Caller.InvalidMove("SpectatorsCannotChat").ConfigureAwait(false);
            return;
        }

        if (!TryAcquireRateLimit(
            nameof(SendChat), _rateLimits.SendChatPermits, _rateLimits.SendChatWindowSeconds))
        {
            await Clients.Caller.InvalidMove("RateLimited").ConfigureAwait(false);
            return;
        }

        await EnsureUserIsParticipantAsync(gameId, userId).ConfigureAwait(false);

        string trimmed = text.Length > MaxChatTextLength
            ? text[..MaxChatTextLength]
            : text;
        string userName = Context.User?.Identity?.Name ?? string.Empty;

        ChatMessageRecord record = new(
            Id: Guid.NewGuid(),
            Scope: ChatScope.Game,
            GameId: gameId,
            UserId: userId,
            Text: trimmed,
            CreatedAt: _clock.UtcNow);

        await _chat.AppendAsync(record, Context.ConnectionAborted).ConfigureAwait(false);

        GameChatMessageDto dto = new(
            record.Id,
            gameId,
            record.UserId,
            userName,
            record.Text,
            record.CreatedAt);

        await Clients.Group(GameGroup(gameId)).ChatMessage(dto).ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Best-effort: each game this connection was in gets a
        // DisconnectCommand. The room decides whether the user actually
        // disconnected (multi-tab clients may have other live connections);
        // this is "one connection just dropped" — Phase 5 reconnect-grace
        // semantics are determined room-side.
        if (Context.Items.TryGetValue(JoinedGamesItemKey, out object? raw)
            && raw is ConcurrentDictionary<Guid, byte> joined)
        {
            Guid userId = TryResolveUserId();
            if (userId != Guid.Empty)
            {
                foreach (Guid gameId in joined.Keys)
                {
                    try
                    {
                        await _orchestrator
                            .EnqueueAsync(new DisconnectCommand(gameId, userId), CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (GameCommandException)
                    {
                        // Game already ended or was removed; nothing to do.
                    }
                }
            }
        }

        _metrics.ConnectedPlayers.Add(-1);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private async Task<GameRecord> EnsureUserIsParticipantAsync(Guid gameId, Guid userId)
    {
        GameRecord? record = await _games.GetAsync(gameId, Context.ConnectionAborted).ConfigureAwait(false)
            ?? throw new HubException($"Game {gameId} not found.");
        if (!record.SeatUserIds.Contains(userId))
        {
            throw new HubException("You are not a participant in this game.");
        }
        return record;
    }

    private Guid ResolveUserId()
    {
        Guid id = TryResolveUserId();
        return id != Guid.Empty
            ? id
            : throw new HubException("Unauthenticated.");
    }

    private Guid TryResolveUserId()
    {
        string? sub = Context.UserIdentifier;
        return sub is not null && Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Wraps the per-connection limiter with a "0 means disabled"
    /// shortcut so test hosts can bypass the policy without touching the
    /// limiter implementation. <paramref name="windowSeconds"/> ≤ 0
    /// admits unconditionally.
    /// </summary>
    private bool TryAcquireRateLimit(string method, int permits, double windowSeconds)
    {
        if (windowSeconds <= 0 || permits <= 0)
        {
            return true;
        }

        // Bucket by Context.UserIdentifier (the JWT `sub` claim), not by
        // connection. A user that opens N WebSockets would otherwise get
        // N× the budget — the audit's H1 finding. Unauthenticated
        // connections shouldn't reach hub methods at all (the [Authorize]
        // attribute on this hub blocks them), but if Context.UserIdentifier
        // is somehow null we fall through to admitting; the engine's
        // own validation is the second line of defense.
        if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
        {
            return true;
        }

        return _rateLimiter.TryAcquire(userId, method, permits, TimeSpan.FromSeconds(windowSeconds));
    }

    private void TrackJoinedGame(Guid gameId) =>
        GetOrCreateSet(JoinedGamesItemKey).TryAdd(gameId, 0);

    private void UntrackJoinedGame(Guid gameId) =>
        TryGetSet(JoinedGamesItemKey)?.TryRemove(gameId, out _);

    private void TrackSpectatedGame(Guid gameId) =>
        GetOrCreateSet(SpectatedGamesItemKey).TryAdd(gameId, 0);

    private void UntrackSpectatedGame(Guid gameId) =>
        TryGetSet(SpectatedGamesItemKey)?.TryRemove(gameId, out _);

    /// <summary>
    /// True when the connection is in the spectator group for
    /// <paramref name="gameId"/> but not the players' group. Drives
    /// the Phase 5.5 spectator-chat policy.
    /// </summary>
    private bool IsSpectatorOnly(Guid gameId)
    {
        bool joined = TryGetSet(JoinedGamesItemKey)?.ContainsKey(gameId) ?? false;
        bool spectating = TryGetSet(SpectatedGamesItemKey)?.ContainsKey(gameId) ?? false;
        return spectating && !joined;
    }

    private ConcurrentDictionary<Guid, byte>? TryGetSet(string key) =>
        Context.Items.TryGetValue(key, out object? raw)
            && raw is ConcurrentDictionary<Guid, byte> set
            ? set
            : null;

    private ConcurrentDictionary<Guid, byte> GetOrCreateSet(string key)
    {
        if (TryGetSet(key) is { } existing)
        {
            return existing;
        }

        ConcurrentDictionary<Guid, byte> created = new();
        Context.Items[key] = created;
        return created;
    }
}
