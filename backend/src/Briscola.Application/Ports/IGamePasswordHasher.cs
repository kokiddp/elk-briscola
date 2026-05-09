namespace Briscola.Application.Ports;

public interface IGamePasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
