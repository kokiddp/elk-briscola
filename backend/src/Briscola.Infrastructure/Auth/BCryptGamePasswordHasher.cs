using Briscola.Application.Ports;

namespace Briscola.Infrastructure.Auth;

/// <summary>
/// Lobby-game password hasher. Distinct from ASP.NET Identity's user-password
/// hashing — different trust boundary (lobby passwords are shared between
/// teammates; user passwords identify the user).
/// </summary>
public sealed class BCryptGamePasswordHasher : IGamePasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(hash);
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
