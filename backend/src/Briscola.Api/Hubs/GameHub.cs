using System.Collections.Concurrent;
using Briscola.Api.Dtos;
using Briscola.Application.Errors;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    private const string JoinedGamesItemKey = "JoinedGames";
    private const int MaxChatTextLength = 500;

    private readonly GameOrchestrator _orchestrator;
    private readonly LobbyService _lobby;
    private readonly IGameRepository _games;
    private readonly IChatRepository _chat;
    private readonly IClock _clock;

    public GameHub(
        GameOrchestrator orchestrator,
        LobbyService lobby,
        IGameRepository games,
        IChatRepository chat,
        IClock clock)
    {
        _orchestrator = orchestrator;
        _lobby = lobby;
        _games = games;
        _chat = chat;
        _clock = clock;
    }

    public static string GameGroup(Guid gameId) => $"{GameGroupPrefix}{gameId}";

    /// <summary>
    /// Joins the <c>game:{gameId}</c> group and enqueues a
    /// <see cref="ReconnectCommand"/> so the room emits a fresh
    /// <c>StateUpdated</c>/<c>Joined</c> snapshot back to the caller.
    /// </summary>
    public async Task JoinGame(Guid gameId)
    {
        Guid userId = ResolveUserId();
        await EnsureUserIsParticipantAsync(gameId, userId).ConfigureAwait(false);

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
        // Persist + broadcast even for spectators here; the spectator
        // policy from Phase 5.5 (reject and emit InvalidMove) lands on
        // top of this when spectator-group membership is wired in 5.2b.
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

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private async Task EnsureUserIsParticipantAsync(Guid gameId, Guid userId)
    {
        GameRecord? record = await _games.GetAsync(gameId, Context.ConnectionAborted).ConfigureAwait(false)
            ?? throw new HubException($"Game {gameId} not found.");
        if (!record.SeatUserIds.Contains(userId))
        {
            throw new HubException("You are not a participant in this game.");
        }
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

    private void TrackJoinedGame(Guid gameId)
    {
        ConcurrentDictionary<Guid, byte> joined = GetOrCreateJoinedSet();
        joined.TryAdd(gameId, 0);
    }

    private void UntrackJoinedGame(Guid gameId)
    {
        if (Context.Items.TryGetValue(JoinedGamesItemKey, out object? raw)
            && raw is ConcurrentDictionary<Guid, byte> joined)
        {
            joined.TryRemove(gameId, out _);
        }
    }

    private ConcurrentDictionary<Guid, byte> GetOrCreateJoinedSet()
    {
        if (Context.Items.TryGetValue(JoinedGamesItemKey, out object? raw)
            && raw is ConcurrentDictionary<Guid, byte> existing)
        {
            return existing;
        }

        ConcurrentDictionary<Guid, byte> set = new();
        Context.Items[JoinedGamesItemKey] = set;
        return set;
    }
}
