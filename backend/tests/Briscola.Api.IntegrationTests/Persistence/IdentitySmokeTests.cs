using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Briscola.Api.IntegrationTests.Persistence;

/// <summary>
/// Confirms that the Identity wiring actually works against a real EF
/// store. SQLite in-memory keeps the test fast (no Testcontainers).
/// Step 3.4 spec: IdentityCore + EntityFrameworkStores wiring + the
/// password / username policy.
/// </summary>
public sealed class IdentitySmokeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public IdentitySmokeTests()
    {
        // Open a SQLite connection ourselves so the in-memory DB outlives
        // the DbContext instances created per scope.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddDataProtection();
        services.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_connection));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BriscolaDbContext>()
            .AddDefaultTokenProviders();

        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Create_user_with_strong_password_succeeds()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "alice",
            Email = "alice@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, "Strong-Pass-123");

        result.Succeeded.Should().BeTrue("create should succeed; got {0}", string.Join(",", result.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "bob",
            Email = "bob@example.com",
            DisplayName = "Bob",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, "short1");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "PasswordTooShort");
    }

    [Fact]
    public async Task Password_without_digit_is_rejected()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "carol",
            Email = "carol@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, "Letters-Only");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "PasswordRequiresDigit");
    }

    [Fact]
    public async Task Username_with_disallowed_character_is_rejected()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "bad name", // space is not in the allow list
            Email = "dave@example.com",
            DisplayName = "Dave",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, "Strong-Pass-123");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "InvalidUserName");
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await users.CreateAsync(new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "first",
            Email = "shared@example.com",
            DisplayName = "First",
            CreatedAt = DateTimeOffset.UtcNow,
        }, "Strong-Pass-123");

        var result = await users.CreateAsync(new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "second",
            Email = "shared@example.com",
            DisplayName = "Second",
            CreatedAt = DateTimeOffset.UtcNow,
        }, "Strong-Pass-123");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task Password_check_succeeds_for_correct_password()
    {
        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "eve",
            Email = "eve@example.com",
            DisplayName = "Eve",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await users.CreateAsync(user, "Strong-Pass-123");

        (await users.CheckPasswordAsync(user, "Strong-Pass-123")).Should().BeTrue();
        (await users.CheckPasswordAsync(user, "wrong-pass-456")).Should().BeFalse();
    }
}
