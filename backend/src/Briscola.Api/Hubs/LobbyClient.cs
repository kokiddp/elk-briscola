using Briscola.Api.Dtos;

namespace Briscola.Api.Hubs;

/// <summary>
/// Server-to-client method surface of <see cref="LobbyHub"/>. The dispatcher
/// (Phase 5.2b) is the only code that should call these methods directly;
/// hub-method handlers below broadcast the local <see cref="ChatMessage"/>
/// echo via <c>Clients.Group(...)</c> at send time.
/// </summary>
public interface ILobbyClient
{
    Task GameCreated(GameSummaryDto summary);

    Task GameUpdated(GameSummaryDto summary);

    Task GameStarted(Guid gameId);

    Task GameEnded(Guid gameId);

    Task ChatMessage(LobbyChatMessageDto message);
}
