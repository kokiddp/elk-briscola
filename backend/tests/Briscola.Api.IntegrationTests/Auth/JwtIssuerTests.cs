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
using Microsoft.Extensions.Options;

namespace Briscola.Api.IntegrationTests.Auth;

public sealed class JwtIssuerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _sp;
    private readonly TestClock _clock = new();

    public JwtIssuerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(NullLoggerProvider.Instance);
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_conn));
        services
            .AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<BriscolaDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IClock>(_clock);
        services.Configure<JwtOptions>(o =>
        {
            // 32 random bytes, base64-encoded — what production-like config looks like.
            o.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            o.Issuer = "test-issuer";
            o.Audience = "test-audience";
            o.AccessTokenLifetimeMinutes = 15;
            o.RefreshTokenLifetimeDays = 14;
        });
        services.AddScoped<JwtIssuer>();

        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<BriscolaDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public void Construction_throws_if_signing_key_missing()
    {
        var opts = Options.Create(new JwtOptions { SigningKey = string.Empty });
        var sb = new ServiceCollection();
        sb.AddSingleton(opts);
        sb.AddDataProtection();
        sb.AddLogging();
        sb.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_conn));
        sb.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<BriscolaDbContext>();
        using var sp = sb.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        Action act = () => _ = new JwtIssuer(opts, _clock, users);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }

    [Fact]
    public void Construction_throws_if_signing_key_too_short()
    {
        var opts = Options.Create(new JwtOptions
        {
            SigningKey = Convert.ToBase64String([1, 2, 3, 4, 5]), // 5 bytes
        });
        var sb = new ServiceCollection();
        sb.AddSingleton(opts);
        sb.AddDataProtection();
        sb.AddLogging();
        sb.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_conn));
        sb.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<BriscolaDbContext>();
        using var sp = sb.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        Action act = () => _ = new JwtIssuer(opts, _clock, users);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void Access_token_carries_required_claims()
    {
        using var scope = _sp.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<JwtIssuer>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "alice",
            DisplayName = "Alice",
            ActiveCardSetId = "placeholder",
            SecurityStamp = "stamp-1",
        };

        var token = issuer.CreateAccessToken(user);

        token.Token.Should().NotBeNullOrEmpty();
        token.ExpiresAt.Should().Be(_clock.UtcNow.AddMinutes(15));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        jwt.Issuer.Should().Be("test-issuer");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("test-audience");
        jwt.Claims.Should().ContainSingle(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == "display_name" && c.Value == "Alice");
        jwt.Claims.Should().ContainSingle(c => c.Type == "card_set" && c.Value == "placeholder");
        jwt.Claims.Should().ContainSingle(c => c.Type == "security_stamp" && c.Value == "stamp-1");
    }

    [Fact]
    public void Refresh_token_hash_is_deterministic_and_token_is_not()
    {
        using var scope = _sp.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<JwtIssuer>();

        var a = issuer.CreateRefreshToken();
        var b = issuer.CreateRefreshToken();

        a.Token.Should().NotBe(b.Token, "refresh tokens must be unique per call");
        a.Hash.Should().Be(JwtIssuer.HashRefreshToken(a.Token));
        b.Hash.Should().Be(JwtIssuer.HashRefreshToken(b.Token));
        a.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(14));
    }

    [Fact]
    public void HashRefreshToken_throws_on_empty_input()
    {
        Action act = () => JwtIssuer.HashRefreshToken(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
