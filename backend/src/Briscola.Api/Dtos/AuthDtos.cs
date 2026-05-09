using System.Diagnostics.CodeAnalysis;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string? DisplayName);

[ExcludeFromCodeCoverage]
public sealed record LoginRequest(
    string UsernameOrEmail,
    string Password);

[ExcludeFromCodeCoverage]
public sealed record RefreshRequest(string RefreshToken);

[ExcludeFromCodeCoverage]
public sealed record LogoutRequest(string RefreshToken);

[ExcludeFromCodeCoverage]
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

[ExcludeFromCodeCoverage]
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
