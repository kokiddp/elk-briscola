namespace Briscola.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; set; } = "briscola";
    public string Audience { get; set; } = "briscola";

    /// <summary>Base64-encoded; minimum 32 bytes after decoding.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 14;
}
