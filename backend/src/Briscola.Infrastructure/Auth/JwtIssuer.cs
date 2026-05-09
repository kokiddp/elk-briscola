using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Briscola.Infrastructure.Auth;

/// <summary>
/// Builds access tokens and refresh tokens. Signing key validation happens
/// at construction time so a misconfigured deployment fails fast.
/// </summary>
public sealed class JwtIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SigningCredentials _credentials;

    public JwtIssuer(IOptions<JwtOptions> options, IClock clock, UserManager<ApplicationUser> users)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _clock = clock;
        _users = users;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:SigningKey is not configured. Set it via the env var Authentication__Jwt__SigningKey.");
        }

        var keyBytes = TryDecodeBase64(_options.SigningKey)
            ?? Encoding.UTF8.GetBytes(_options.SigningKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"Authentication:Jwt:SigningKey must be at least 32 bytes; got {keyBytes.Length}.");
        }

        var key = new SymmetricSecurityKey(keyBytes);
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new("display_name", user.DisplayName),
            new("card_set", user.ActiveCardSetId),
            new("security_stamp", user.SecurityStamp ?? string.Empty),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);

        var serialized = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(serialized, expires);
    }

    /// <summary>
    /// Generates a fresh refresh token. Returns the plain token (returned to
    /// the client once), the SHA-256 hash (persisted), and the expiry.
    /// </summary>
    public RefreshTokenSecret CreateRefreshToken()
    {
        var raw = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(raw);
        var hash = HashRefreshToken(token);
        var expires = _clock.UtcNow.AddDays(_options.RefreshTokenLifetimeDays);
        return new RefreshTokenSecret(token, hash, expires);
    }

    public static string HashRefreshToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>The user's SecurityStamp; refreshed on password change to invalidate outstanding tokens.</summary>
    public async Task<string?> GetCurrentSecurityStampAsync(ApplicationUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await _users.GetSecurityStampAsync(user).ConfigureAwait(false);
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// What the issuer hands back when minting a refresh token: the raw token
/// (return-once to the client), the hash (persist), and the expiry.
/// </summary>
public sealed record RefreshTokenSecret(string Token, string Hash, DateTimeOffset ExpiresAt);
