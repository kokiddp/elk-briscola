using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Lobby;

[ExcludeFromCodeCoverage]
public sealed record CreateGameRequest(
    GameMode Mode,
    string Name,
    bool IsPrivate,
    string? Password);
