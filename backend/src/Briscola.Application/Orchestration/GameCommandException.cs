namespace Briscola.Application.Orchestration;

public sealed class GameCommandException : Exception
{
    public GameCommandException(string message)
        : base(message)
    {
    }
}
