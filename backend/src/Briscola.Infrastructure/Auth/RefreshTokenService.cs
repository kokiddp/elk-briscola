using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Auth;

public sealed record IssuedTokenPair(AccessToken Access, RefreshTokenSecret Refresh);

public enum RefreshFailureReason
{
    NotFound,
    Expired,
    Revoked,
    UserMissing,
    /// <summary>Reused a revoked token — chain has been revoked as a precaution.</summary>
    Reuse,
}

public abstract record RefreshResult
{
    private RefreshResult() { }
    public sealed record Success(IssuedTokenPair Tokens) : RefreshResult;
    public sealed record Failure(RefreshFailureReason Reason) : RefreshResult;
}

/// <summary>
/// Rotates refresh tokens. Atomic in a transaction: mark the old token
/// revoked with ReplacedByTokenId, persist the new one, mint a new access
/// token. If a revoked token is presented again, every descendant in the
/// rotation chain is revoked (defense against token theft).
/// </summary>
public sealed class RefreshTokenService(
    BriscolaDbContext db,
    JwtIssuer issuer,
    IClock clock,
    UserManager<ApplicationUser> users)
{
    public async Task<RefreshTokenSecret> IssueAsync(Guid userId, CancellationToken ct)
    {
        var secret = issuer.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = secret.Hash,
            ExpiresAt = secret.ExpiresAt,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return secret;
    }

    public async Task<RefreshResult> RotateAsync(string presentedToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        var hash = JwtIssuer.HashRefreshToken(presentedToken);
        var now = clock.UtcNow;

        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return new RefreshResult.Failure(RefreshFailureReason.NotFound);
        }

        if (existing.ExpiresAt < now)
        {
            return new RefreshResult.Failure(RefreshFailureReason.Expired);
        }

        if (existing.RevokedAt is not null)
        {
            // Reuse of a revoked token — revoke the entire descendant chain.
            await RevokeDescendantsAsync(existing.Id, now, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new RefreshResult.Failure(RefreshFailureReason.Reuse);
        }

        var user = await users.FindByIdAsync(existing.UserId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return new RefreshResult.Failure(RefreshFailureReason.UserMissing);
        }

        var newSecret = issuer.CreateRefreshToken();
        var newId = Guid.NewGuid();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = newId,
            UserId = user.Id,
            TokenHash = newSecret.Hash,
            ExpiresAt = newSecret.ExpiresAt,
        });
        existing.RevokedAt = now;
        existing.ReplacedByTokenId = newId;

        var newAccess = issuer.CreateAccessToken(user);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RefreshResult.Success(new IssuedTokenPair(newAccess, newSecret));
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        var hash = JwtIssuer.HashRefreshToken(presentedToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            .ConfigureAwait(false);
        if (existing is null || existing.RevokedAt is not null)
        {
            return;
        }

        existing.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task RevokeDescendantsAsync(Guid rootId, DateTimeOffset now, CancellationToken ct)
    {
        // Walk the chain forward via ReplacedByTokenId. We process each row's
        // own revocation status, then enqueue its replacement. The seen-set
        // is populated as we visit so a malformed cycle can't loop forever.
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id))
            {
                continue;
            }

            var row = await db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == id, ct)
                .ConfigureAwait(false);
            if (row is null)
            {
                continue;
            }

            if (row.RevokedAt is null)
            {
                row.RevokedAt = now;
            }
            if (row.ReplacedByTokenId is { } next)
            {
                queue.Enqueue(next);
            }
        }
    }
}
