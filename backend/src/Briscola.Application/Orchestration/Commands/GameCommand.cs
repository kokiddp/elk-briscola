using Briscola.Domain.Primitives;

namespace Briscola.Application.Orchestration.Commands;

public abstract record GameCommand(Guid GameId);

public sealed record JoinGameCommand(Guid GameId, Guid UserId, int? PreferredSeat) : GameCommand(GameId);

public sealed record LeaveGameCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

public sealed record PlayCardCommand(Guid GameId, Guid UserId, Card Card) : GameCommand(GameId);

public sealed record ViewOwnPileCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

public sealed record DisconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

public sealed record ReconnectCommand(Guid GameId, Guid UserId) : GameCommand(GameId);

public sealed record IdleTickCommand(Guid GameId, DateTimeOffset At) : GameCommand(GameId);

public sealed record ForfeitOnDisconnectCommand(Guid GameId, int SeatIndex) : GameCommand(GameId);
