using Briscola.Api.Dtos;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Briscola.Api.Hubs;

/// <summary>
/// Real-time channel for the lobby screen. Authenticated users join the
/// <c>lobby:open</c> group to receive open-game create/update/start/end
/// notifications and chat. The lobby fan-out from server-side state
/// changes (game created, etc.) is the <see cref="GameEventDispatcher"/>
/// territory in Phase 5.2b; this hub only owns group membership and the
/// chat round-trip.
/// </summary>
[Authorize]
public sealed class LobbyHub : Hub<ILobbyClient>
{
    public const string OpenLobbyGroup = "lobby:open";
    private const int MaxChatTextLength = 500;

    private readonly IChatRepository _chat;
    private readonly IClock _clock;

    public LobbyHub(IChatRepository chat, IClock clock)
    {
        _chat = chat;
        _clock = clock;
    }

    public Task SubscribeOpen() =>
        Groups.AddToGroupAsync(Context.ConnectionId, OpenLobbyGroup, Context.ConnectionAborted);

    public Task UnsubscribeOpen() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OpenLobbyGroup, Context.ConnectionAborted);

    public async Task SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string trimmed = text.Length > MaxChatTextLength
            ? text[..MaxChatTextLength]
            : text;

        Guid userId = ResolveUserId();
        string userName = Context.User?.Identity?.Name ?? string.Empty;

        ChatMessageRecord record = new(
            Id: Guid.NewGuid(),
            Scope: ChatScope.Lobby,
            GameId: null,
            UserId: userId,
            Text: trimmed,
            CreatedAt: _clock.UtcNow);

        await _chat.AppendAsync(record, Context.ConnectionAborted).ConfigureAwait(false);

        LobbyChatMessageDto dto = new(
            record.Id,
            record.UserId,
            userName,
            record.Text,
            record.CreatedAt);

        await Clients.Group(OpenLobbyGroup)
            .ChatMessage(dto)
            .ConfigureAwait(false);
    }

    private Guid ResolveUserId()
    {
        string? sub = Context.UserIdentifier;
        return sub is not null && Guid.TryParse(sub, out Guid id)
            ? id
            : throw new HubException("Unauthenticated.");
    }
}
