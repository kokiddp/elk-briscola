namespace Briscola.Application.Ports;

public interface IRandomSourceFactory
{
    IRandomSource Create();
}
