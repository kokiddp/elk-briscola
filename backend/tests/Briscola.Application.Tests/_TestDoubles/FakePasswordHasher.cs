using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class FakePasswordHasher : IGamePasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string hash) => hash == Hash(password);
}
