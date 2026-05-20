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

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another rotate of the same parent token beat us to it. EF
            // rolls back the staged INSERT of the new child along with
            // the failed UPDATE of existing.RevokedAt. Treat it as a
            // reuse attempt — the client is presenting a token that's
            // now revoked by the winning sibling.
            return new RefreshResult.Failure(RefreshFailureReason.Reuse);
        }

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
        // One round-trip to materialise every token belonging to this
        // user, then walk the (Id → ReplacedByTokenId) chain entirely
        // in memory. The previous design did one SQL query per
        // descendant — on a long chain that's a per-link RTT, plus
        // every replay attack is uncapped at the DB. We pin a small
        // hop cap as a defense-in-depth against malformed chains; the
        // seen-set catches cycles separately.
        const int MaxChainHops = 64;

        // Get the root first so we have its UserId; if it's gone there's
        // nothing to revoke.
        RefreshTokenEntity? root = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == rootId, ct)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        Dictionary<Guid, RefreshTokenEntity> byId = await db.RefreshTokens
            .Where(t => t.UserId == root.UserId)
            .ToDictionaryAsync(t => t.Id, ct)
            .ConfigureAwait(false);

        HashSet<Guid> seen = [];
        Guid? cursor = rootId;
        int hops = 0;
        while (cursor is { } id && hops++ < MaxChainHops && seen.Add(id))
        {
            if (!byId.TryGetValue(id, out RefreshTokenEntity? row))
            {
                break;
            }
            if (row.RevokedAt is null)
            {
                row.RevokedAt = now;
            }
            cursor = row.ReplacedByTokenId;
        }
    }
}
