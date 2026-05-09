using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Orchestration.Commands;

[ExcludeFromCodeCoverage]
public abstract record GameCommand(Guid GameId);

[ExcludeFromCodeCoverage]
public sealed record JoinGameCommand(Guid GameId, Guid UserId, int? PreferredSeat) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record LeaveGameCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record PlayCardCommand(Guid GameId, Guid UserId, Card Card) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record ViewOwnPileCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record DisconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record ReconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record IdleTickCommand(Guid GameId, DateTimeOffset At) : GameCommand(GameId);

[ExcludeFromCodeCoverage]
public sealed record ForfeitOnDisconnectCommand(Guid GameId, int SeatIndex) : GameCommand(GameId);
