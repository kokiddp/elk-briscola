namespace Briscola.Application.Errors;

public abstract class BriscolaApplicationException : Exception
{
    protected BriscolaApplicationException(string message)
        : base(message)
    {
    }
}

public sealed class GameNotFoundException(Guid gameId)
    : BriscolaApplicationException($"Game {gameId} was not found.")
{
    public Guid GameId { get; } = gameId;
}

public sealed class LobbyConflictException(string message) : BriscolaApplicationException(message);

public sealed class InvalidPasswordException()
    : BriscolaApplicationException("The supplied game password is invalid.");

public sealed class ConcurrencyConflictException(string message) : BriscolaApplicationException(message);
