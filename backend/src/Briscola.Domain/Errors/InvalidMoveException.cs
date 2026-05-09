namespace Briscola.Domain.Errors;

public sealed class InvalidMoveException : Exception
{
    public InvalidMoveException(InvalidMoveCode code, string? message = null)
        : base(message ?? code.ToString())
    {
        Code = code;
    }

    public InvalidMoveCode Code { get; }
}
