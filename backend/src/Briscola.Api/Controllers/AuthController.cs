using Briscola.Api.Dtos;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Application.Ranking;
using Briscola.Infrastructure.Auth;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtIssuer _issuer;
    private readonly RefreshTokenService _refreshTokens;
    private readonly IRankingRepository _rankings;
    private readonly IClock _clock;
    private readonly BriscolaDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> users,
        JwtIssuer issuer,
        RefreshTokenService refreshTokens,
        IRankingRepository rankings,
        IClock clock,
        BriscolaDbContext db)
    {
        _users = users;
        _issuer = issuer;
        _refreshTokens = refreshTokens;
        _rankings = rankings;
        _clock = clock;
        _db = db;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthRegister)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username : request.DisplayName!,
            CreatedAt = _clock.UtcNow,
        };

        IdentityResult result = await _users.CreateAsync(user, request.Password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Conflict(new
            {
                code = "RegistrationFailed",
                errors = result.Errors.Select(e => new { e.Code, e.Description }),
            });
        }

        // Seed a default ranking row so /me/ranking returns the canonical 1500.
        await _rankings.GetAsync(user.Id, ct).ConfigureAwait(false);

        return CreatedAtAction(
            actionName: nameof(MeController.Get),
            controllerName: "Me",
            routeValues: null,
            value: new { id = user.Id, username = user.UserName });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthLogin)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser? user = await ResolveAsync(request.UsernameOrEmail).ConfigureAwait(false);
        if (user is null
            || !await _users.CheckPasswordAsync(user, request.Password).ConfigureAwait(false))
        {
            return Unauthorized(new { code = "InvalidCredentials" });
        }

        AccessToken access = _issuer.CreateAccessToken(user);
        RefreshTokenSecret refresh = await _refreshTokens.IssueAsync(user.Id, ct).ConfigureAwait(false);

        return Ok(new TokenResponse(
            access.Token, access.ExpiresAt,
            refresh.Token, refresh.ExpiresAt));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthRefresh)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        RefreshResult result = await _refreshTokens.RotateAsync(request.RefreshToken, ct).ConfigureAwait(false);
        return result switch
        {
            RefreshResult.Success success => Ok(new TokenResponse(
                success.Tokens.Access.Token, success.Tokens.Access.ExpiresAt,
                success.Tokens.Refresh.Token, success.Tokens.Refresh.ExpiresAt)),
            RefreshResult.Failure { Reason: var reason } => Unauthorized(new
            {
                code = $"Refresh{reason}",
            }),
            _ => Unauthorized(new { code = "RefreshUnknown" }),
        };
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _refreshTokens.RevokeAsync(request.RefreshToken, ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken _)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        IdentityResult result = await _users
            .ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                code = "ChangePasswordFailed",
                errors = result.Errors.Select(e => new { e.Code, e.Description }),
            });
        }

        return NoContent();
    }

    private async Task<ApplicationUser?> ResolveAsync(string usernameOrEmail)
    {
        if (usernameOrEmail.Contains('@', StringComparison.Ordinal))
        {
            ApplicationUser? byEmail = await _db.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == _users.NormalizeEmail(usernameOrEmail))
                .ConfigureAwait(false);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        return await _users.FindByNameAsync(usernameOrEmail).ConfigureAwait(false);
    }
}
