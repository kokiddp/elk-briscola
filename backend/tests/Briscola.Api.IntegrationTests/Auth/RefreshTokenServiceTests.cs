using System.Security.Cryptography;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Auth;
using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Briscola.Api.IntegrationTests.Auth;

public sealed class RefreshTokenServiceTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _conn = null!;
    private ServiceProvider _sp = null!;
    private TestClock _clock = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _clock = new TestClock();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(NullLoggerProvider.Instance);
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_conn));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<BriscolaDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IClock>(_clock);
        services.Configure<JwtOptions>(o =>
        {
            o.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            o.Issuer = "test";
            o.Audience = "test";
            o.AccessTokenLifetimeMinutes = 15;
            o.RefreshTokenLifetimeDays = 14;
        });
        services.AddScoped<JwtIssuer>();
        services.AddScoped<RefreshTokenService>();

        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _userId = Guid.NewGuid();
        var result = await users.CreateAsync(new ApplicationUser
        {
            Id = _userId,
            UserName = "alice",
            Email = "alice@example.com",
            DisplayName = "Alice",
            CreatedAt = _clock.UtcNow,
        }, "Strong-Pass-123");
        result.Succeeded.Should().BeTrue();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _sp?.Dispose();
        _conn?.Dispose();
    }

    [Fact]
    public async Task Issue_persists_a_token_with_expiry()
    {
        using var scope = _sp.CreateScope();
        var rts = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();

        var secret = await rts.IssueAsync(_userId, CancellationToken.None);

        secret.Token.Should().NotBeNullOrEmpty();
        secret.Hash.Should().Be(JwtIssuer.HashRefreshToken(secret.Token));
        secret.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(14));

        var ctx = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        var stored = await ctx.RefreshTokens.SingleAsync();
        stored.TokenHash.Should().Be(secret.Hash);
        stored.UserId.Should().Be(_userId);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rotate_swaps_token_and_marks_old_revoked()
    {
        RefreshTokenSecret first;
        using (var s = _sp.CreateScope())
        {
            first = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .IssueAsync(_userId, CancellationToken.None);
        }

        RefreshResult rotated;
        using (var s = _sp.CreateScope())
        {
            rotated = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(first.Token, CancellationToken.None);
        }

        var success = rotated.Should().BeOfType<RefreshResult.Success>().Subject;
        success.Tokens.Refresh.Token.Should().NotBe(first.Token);
        success.Tokens.Access.Token.Should().NotBeNullOrEmpty();

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        var oldRow = await ctx.RefreshTokens.SingleAsync(t => t.TokenHash == first.Hash);
        oldRow.RevokedAt.Should().NotBeNull();
        oldRow.ReplacedByTokenId.Should().NotBeNull();
        var newRow = await ctx.RefreshTokens.SingleAsync(t => t.TokenHash == success.Tokens.Refresh.Hash);
        newRow.RevokedAt.Should().BeNull();
        oldRow.ReplacedByTokenId.Should().Be(newRow.Id);
    }

    [Fact]
    public async Task Rotate_with_unknown_token_returns_NotFound()
    {
        using var scope = _sp.CreateScope();
        var rts = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();

        var result = await rts.RotateAsync(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            CancellationToken.None);

        var failure = result.Should().BeOfType<RefreshResult.Failure>().Subject;
        failure.Reason.Should().Be(RefreshFailureReason.NotFound);
    }

    [Fact]
    public async Task Rotate_with_expired_token_returns_Expired()
    {
        RefreshTokenSecret first;
        using (var s = _sp.CreateScope())
        {
            first = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .IssueAsync(_userId, CancellationToken.None);
        }

        _clock.Advance(TimeSpan.FromDays(15));

        RefreshResult result;
        using (var s = _sp.CreateScope())
        {
            result = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(first.Token, CancellationToken.None);
        }

        result.Should().BeOfType<RefreshResult.Failure>()
            .Which.Reason.Should().Be(RefreshFailureReason.Expired);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_returns_Reuse_and_revokes_chain()
    {
        // Issue → Rotate (issues T2) → Rotate (issues T3) → Replay T1 (revoked).
        // T1 is the original; rotating it once revokes it. Re-presenting it
        // should fail with Reuse and propagate revocation to the chain.
        RefreshTokenSecret t1;
        using (var s = _sp.CreateScope())
        {
            t1 = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .IssueAsync(_userId, CancellationToken.None);
        }

        RefreshResult.Success step1;
        using (var s = _sp.CreateScope())
        {
            step1 = (RefreshResult.Success)await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(t1.Token, CancellationToken.None);
        }

        RefreshResult.Success step2;
        using (var s = _sp.CreateScope())
        {
            step2 = (RefreshResult.Success)await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(step1.Tokens.Refresh.Token, CancellationToken.None);
        }

        // Replay the original (already revoked).
        RefreshResult replayed;
        using (var s = _sp.CreateScope())
        {
            replayed = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(t1.Token, CancellationToken.None);
        }

        replayed.Should().BeOfType<RefreshResult.Failure>()
            .Which.Reason.Should().Be(RefreshFailureReason.Reuse);

        // The full chain is now revoked.
        using var verify = _sp.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        foreach (var hash in new[] { t1.Hash, step1.Tokens.Refresh.Hash, step2.Tokens.Refresh.Hash })
        {
            var row = await ctx.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
            row.RevokedAt.Should().NotBeNull("token {0} must be revoked after reuse detection", hash);
        }
    }

    [Fact]
    public async Task Revoke_marks_token_revoked()
    {
        RefreshTokenSecret first;
        using (var s = _sp.CreateScope())
        {
            first = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .IssueAsync(_userId, CancellationToken.None);
        }

        using (var s = _sp.CreateScope())
        {
            await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RevokeAsync(first.Token, CancellationToken.None);
        }

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        var row = await ctx.RefreshTokens.SingleAsync();
        row.RevokedAt.Should().NotBeNull();

        // Revoking again is a no-op (does not throw).
        using var s2 = _sp.CreateScope();
        await s2.ServiceProvider.GetRequiredService<RefreshTokenService>()
            .RevokeAsync(first.Token, CancellationToken.None);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
