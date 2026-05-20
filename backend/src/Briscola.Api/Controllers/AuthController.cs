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

/// <summary>
/// Account lifecycle and JWT issuance. All <c>/auth/*</c> endpoints are
/// rate-limited (see <c>Briscola.Api.RateLimiting.RateLimitingPolicies</c>);
/// register and login are anonymous, the rest require a valid bearer token.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Tags("Auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtIssuer _issuer;
    private readonly RefreshTokenService _refreshTokens;
    private readonly IRankingRepository _rankings;
    private readonly IClock _clock;
    private readonly BriscolaDbContext _db;

    /// <summary>
    /// PBKDF2 hash used to equalize Login's user-not-found path against
    /// the happy path. Lazy-initialized off the configured PasswordHasher
    /// so the timing matches whatever cost factor Identity is set to.
    /// </summary>
    private static string? _dummyHash;
    private static readonly object _dummyHashGate = new();
    private string DummyHash
    {
        get
        {
            if (_dummyHash is not null) return _dummyHash;
            lock (_dummyHashGate)
            {
                _dummyHash ??= _users.PasswordHasher
                    .HashPassword(new ApplicationUser(), "timing-equalizer-sentinel-pass-1");
            }
            return _dummyHash;
        }
    }

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

    /// <summary>Register a new account.</summary>
    /// <remarks>
    /// Returns <c>201 Created</c> on success with a Location header pointing
    /// to <c>/api/v1/me</c>. Conflicts on duplicate username/email surface as
    /// <c>409</c> with a <c>code: "RegistrationFailed"</c> body and an
    /// <c>errors</c> array straight from ASP.NET Identity.
    /// </remarks>
    /// <response code="201">Account created; client should now call /auth/login.</response>
    /// <response code="409">Username or email already taken, or password policy failed.</response>
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

    /// <summary>Exchange username/email + password for an access/refresh token pair.</summary>
    /// <remarks>
    /// The access token is short-lived (default 15 min) and goes in the
    /// <c>Authorization: Bearer …</c> header for subsequent calls. The
    /// refresh token (default 14 d) is stored client-side and used against
    /// <c>/auth/refresh</c> to rotate the pair.
    /// </remarks>
    /// <response code="200">Tokens issued.</response>
    /// <response code="401">Unknown user or wrong password (no information leak about which).</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthLogin)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser? user = await ResolveAsync(request.UsernameOrEmail).ConfigureAwait(false);
        bool passwordOk;
        if (user is null)
        {
            // Equalize timing on the user-not-found branch — without
            // this an attacker can distinguish "no such user" from
            // "wrong password" by the response latency (no PBKDF2 work
            // happens when CheckPasswordAsync is skipped). Hash the
            // presented password against a precomputed sentinel hash
            // so the cost matches the happy path's verify. Audit M7.
            _ = _users.PasswordHasher.VerifyHashedPassword(
                new ApplicationUser(), DummyHash, request.Password);
            passwordOk = false;
        }
        else
        {
            passwordOk = await _users.CheckPasswordAsync(user, request.Password).ConfigureAwait(false);
        }

        if (user is null || !passwordOk)
        {
            return Unauthorized(new { code = "InvalidCredentials" });
        }

        AccessToken access = _issuer.CreateAccessToken(user);
        RefreshTokenSecret refresh = await _refreshTokens.IssueAsync(user.Id, ct).ConfigureAwait(false);

        return Ok(new TokenResponse(
            access.Token, access.ExpiresAt,
            refresh.Token, refresh.ExpiresAt));
    }

    /// <summary>Rotate the refresh token, returning a fresh access/refresh pair.</summary>
    /// <remarks>
    /// Implements rotation chains: the supplied refresh token is invalidated
    /// and a new one is issued. Re-using a previously rotated token is
    /// rejected (chain-replay defense).
    /// </remarks>
    /// <response code="200">New access + refresh tokens.</response>
    /// <response code="401">Refresh token unknown, expired, revoked, or already rotated.</response>
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

    /// <summary>Revoke a refresh token (best-effort — succeeds silently if the token is unknown).</summary>
    /// <response code="204">Token revoked or already absent.</response>
    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthLogout)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _refreshTokens.RevokeAsync(request.RefreshToken, ct).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Change the authenticated user's password.</summary>
    /// <remarks>
    /// Identity bumps the user's <c>SecurityStamp</c> on success, which
    /// invalidates every still-valid access token issued before this call
    /// (validated on the next request via <c>SecurityStampValidator</c>).
    /// Existing refresh tokens stay valid until they're rotated or revoked.
    /// </remarks>
    /// <response code="204">Password changed; outstanding access tokens are now invalidated.</response>
    /// <response code="400">Current password wrong, or new password violates policy.</response>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthChangePassword)]
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

    /// <summary>Change the authenticated user's email.</summary>
    /// <remarks>
    /// Requires re-presenting the current password — protects against a
    /// stolen access token quietly re-binding the account to an
    /// attacker-controlled inbox. Identity bumps the user's
    /// <c>SecurityStamp</c> on success, invalidating every still-valid
    /// access token issued before this call. The new email must not
    /// already be in use; conflicts come back as 409.
    /// </remarks>
    /// <response code="204">Email changed; outstanding access tokens are now invalidated.</response>
    /// <response code="400">Current password wrong, new email malformed, or empty.</response>
    /// <response code="409">An account with that email already exists.</response>
    [HttpPost("change-email")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.RateLimitingPolicies.AuthChangeEmail)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        // Step 1: confirm the caller actually knows the current password.
        // Without this an attacker with a stolen access token could silently
        // rebind the account to an inbox they own and then trigger
        // password reset elsewhere.
        if (!await _users.CheckPasswordAsync(user, request.CurrentPassword).ConfigureAwait(false))
        {
            return BadRequest(new { code = "InvalidCurrentPassword" });
        }

        // Step 2: validation that's cheaper than the duplicate-check round-trip.
        if (string.IsNullOrWhiteSpace(request.NewEmail))
        {
            return BadRequest(new { code = "EmailRequired" });
        }

        string normalized = _users.NormalizeEmail(request.NewEmail);
        if (string.Equals(normalized, user.NormalizedEmail, StringComparison.Ordinal))
        {
            // Same email — no-op, but return success so an idempotent
            // client retry doesn't get a confusing failure.
            return NoContent();
        }

        // Step 3: uniqueness pre-check so we can return 409 distinctly
        // from validation failures. UserManager.SetEmailAsync doesn't
        // enforce uniqueness in the normal flow — Identity expects you
        // to wire confirmation tokens. We're skipping confirmation for
        // v1 (no email infra) so the controller does the check directly.
        bool taken = await _db.Users
            .AnyAsync(u => u.Id != user.Id && u.NormalizedEmail == normalized, ct)
            .ConfigureAwait(false);
        if (taken)
        {
            return Conflict(new { code = "EmailAlreadyTaken" });
        }

        IdentityResult result = await _users.SetEmailAsync(user, request.NewEmail).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                code = "ChangeEmailFailed",
                errors = result.Errors.Select(e => new { e.Code, e.Description }),
            });
        }

        // Bump the security stamp so outstanding access tokens are
        // invalidated on the next request — same posture as
        // change-password. SetEmailAsync only bumps the stamp when
        // RequireConfirmedEmail is on; we want it bumped unconditionally
        // because the account binding changed.
        await _users.UpdateSecurityStampAsync(user).ConfigureAwait(false);

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
