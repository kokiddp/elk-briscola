using System.IdentityModel.Tokens.Jwt;
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

/// <summary>
/// End-to-end auth flow at the service level: register → log in → refresh
/// → change password → confirm SecurityStamp invalidates outstanding access
/// tokens. Closes the Step 3.8 spec at the level Phase 3 can verify; the
/// HTTP-controller variant of these tests will land in Phase 4 with
/// WebApplicationFactory.
///
/// Note on Postgres: the spec called for Testcontainers Postgres. The dev
/// environment doesn't have Docker available (WSL without Docker Desktop),
/// so we run on SQLite in-memory. The SchemaParityTests already prove the
/// EF model is congruent across providers; switching this fixture to
/// Testcontainers later (or adding a parallel fixture) is a Phase 11
/// hardening task.
/// </summary>
public sealed class AuthFlowSmokeTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _conn = null!;
    private ServiceProvider _sp = null!;
    private TestClock _clock = null!;

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
        services.AddIdentityCore<ApplicationUser>(opts =>
            {
                opts.Password.RequireDigit = true;
                opts.Password.RequiredLength = 10;
                opts.Password.RequireNonAlphanumeric = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireLowercase = false;
                opts.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
                opts.User.RequireUniqueEmail = true;
            })
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
    public async Task Register_then_login_then_refresh_then_password_change_invalidates_old_access()
    {
        var userId = Guid.NewGuid();
        const string username = "alice";
        const string startingPassword = "Strong-Pass-123";
        const string newPassword = "Stronger-Pass-456";

        // 1. Register
        IdentityResult registered;
        using (var s = _sp.CreateScope())
        {
            var users = s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            registered = await users.CreateAsync(new ApplicationUser
            {
                Id = userId,
                UserName = username,
                Email = "alice@example.com",
                DisplayName = "Alice",
                CreatedAt = _clock.UtcNow,
            }, startingPassword);
        }
        registered.Succeeded.Should().BeTrue();

        // 2. Log in (CheckPassword + mint access + issue refresh)
        AccessToken initialAccess;
        RefreshTokenSecret initialRefresh;
        using (var s = _sp.CreateScope())
        {
            var users = s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(username);
            user.Should().NotBeNull();
            (await users.CheckPasswordAsync(user!, startingPassword)).Should().BeTrue();

            var issuer = s.ServiceProvider.GetRequiredService<JwtIssuer>();
            var rts = s.ServiceProvider.GetRequiredService<RefreshTokenService>();
            initialAccess = issuer.CreateAccessToken(user!);
            initialRefresh = await rts.IssueAsync(user!.Id, CancellationToken.None);
        }
        initialAccess.Token.Should().NotBeNullOrEmpty();
        var initialAccessStamp = ReadSecurityStamp(initialAccess.Token);

        // 3. Rotate the refresh token
        AccessToken rotatedAccess;
        RefreshTokenSecret rotatedRefresh;
        using (var s = _sp.CreateScope())
        {
            var rts = s.ServiceProvider.GetRequiredService<RefreshTokenService>();
            var result = await rts.RotateAsync(initialRefresh.Token, CancellationToken.None);
            var success = result.Should().BeOfType<RefreshResult.Success>().Subject;
            rotatedAccess = success.Tokens.Access;
            rotatedRefresh = success.Tokens.Refresh;
        }
        rotatedAccess.Token.Should().NotBeNullOrEmpty();

        // 4. Replaying the original refresh token now fails as Reuse and
        // revokes the chain.
        using (var s = _sp.CreateScope())
        {
            var replay = await s.ServiceProvider.GetRequiredService<RefreshTokenService>()
                .RotateAsync(initialRefresh.Token, CancellationToken.None);
            replay.Should().BeOfType<RefreshResult.Failure>()
                .Which.Reason.Should().Be(RefreshFailureReason.Reuse);
        }

        // 5. Change password — SecurityStamp rotates.
        string? freshStamp;
        using (var s = _sp.CreateScope())
        {
            var users = s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            (await users.ChangePasswordAsync(user!, startingPassword, newPassword))
                .Succeeded.Should().BeTrue();
            freshStamp = await users.GetSecurityStampAsync(user!);
        }

        // The stamp baked into the *initial* access token must no longer
        // match the user's current stamp. That is the Phase 4 validator's
        // hook for invalidating outstanding access tokens on password change.
        initialAccessStamp.Should().NotBe(freshStamp,
            "password change must rotate SecurityStamp so old access tokens fail validation");

        // The access token issued during the rotation in step 3 was minted
        // *before* the password change too — it also gets invalidated by
        // the same SecurityStamp rotation. Confirm.
        ReadSecurityStamp(rotatedAccess.Token).Should().NotBe(freshStamp);

        // 6. Logging in with the new password works; the new access token
        // carries the fresh stamp.
        using (var s = _sp.CreateScope())
        {
            var users = s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            (await users.CheckPasswordAsync(user!, newPassword)).Should().BeTrue();

            var issuer = s.ServiceProvider.GetRequiredService<JwtIssuer>();
            var newAccess = issuer.CreateAccessToken(user!);
            ReadSecurityStamp(newAccess.Token).Should().Be(freshStamp);
        }
    }

    private static string ReadSecurityStamp(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        return token.Claims.Single(c => c.Type == "security_stamp").Value;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
