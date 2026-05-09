using Briscola.Domain.Primitives;

namespace Briscola.Application.Lobby;

public sealed record CreateGameRequest(
    GameMode Mode,
    string Name,
    bool IsPrivate,
    string? Password);
