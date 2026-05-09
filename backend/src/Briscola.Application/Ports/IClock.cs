namespace Briscola.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
