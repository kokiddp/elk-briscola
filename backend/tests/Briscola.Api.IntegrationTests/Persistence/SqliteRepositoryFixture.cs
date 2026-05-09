using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Briscola.Api.IntegrationTests.Persistence;

/// <summary>
/// SQLite-in-memory fixture for repository tests. Holds the connection open
/// so the in-memory DB outlives per-scope DbContexts; constructs a small DI
/// graph with the EF repositories under test and a deterministic test clock.
/// </summary>
internal sealed class SqliteRepositoryFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestClock Clock { get; } = new();
    public ServiceProvider Services { get; }

    public SqliteRepositoryFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(NullLoggerProvider.Instance);
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<BriscolaDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<Briscola.Infrastructure.Persistence.Entities.ApplicationUser>()
            .AddEntityFrameworkStores<BriscolaDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IClock>(Clock);
        services.AddScoped<Briscola.Infrastructure.Persistence.Repositories.EfGameRepository>();
        services.AddScoped<Briscola.Infrastructure.Persistence.Repositories.EfChatRepository>();
        services.AddScoped<Briscola.Infrastructure.Persistence.Repositories.EfRankingRepository>();

        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BriscolaDbContext>().Database.EnsureCreated();
    }

    public IServiceScope NewScope() => Services.CreateScope();

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }

    internal sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
