using System.Data.Common;
using Briscola.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Boots the real <c>Briscola.Api</c> host with a per-fixture, in-memory
/// SQLite connection. Each fixture instance gets its own database so
/// individual <c>[Fact]</c>s don't observe each other's users / games.
/// (Testcontainers Postgres is the Phase 11 hardening follow-up.)
/// </summary>
public sealed class BriscolaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection? _conn;

    public string Base64SigningKey { get; } =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _conn?.Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Stay in Production env (so dev-only Swagger + file logger don't
        // run) but disable the HTTPS redirect that would 308 POSTs to
        // https:// and break the in-memory test client.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("DISABLE_HTTPS_REDIRECTION", "true");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
                ["Authentication:Jwt:SigningKey"] = Base64SigningKey,
                ["Authentication:Jwt:Issuer"] = "briscola-test",
                ["Authentication:Jwt:Audience"] = "briscola-test",
                // Apply migrations on startup so the long-lived in-memory
                // SQLite connection has the full schema before the first
                // request. This shadows the appsettings.Development.json
                // value, but matches it.
                ["Migrations:RunOnStartup"] = "true",
                ["Cors:AllowedOrigins:0"] = "http://localhost",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap the SQLite-file DbContext registration for one bound to
            // the long-lived in-memory connection so the schema persists
            // between requests.
            ServiceDescriptor? dbOptions = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BriscolaDbContext>));
            if (dbOptions is not null)
            {
                services.Remove(dbOptions);
            }

            ServiceDescriptor? dbContext = services.SingleOrDefault(
                d => d.ServiceType == typeof(BriscolaDbContext));
            if (dbContext is not null)
            {
                services.Remove(dbContext);
            }

            services.AddSingleton<DbConnection>(_conn!);
            services.AddDbContext<BriscolaDbContext>((sp, opts) =>
            {
                opts.UseSqlite(sp.GetRequiredService<DbConnection>(),
                    b => b.MigrationsAssembly("Briscola.Infrastructure.Sqlite.Migrations"));
            });
        });
    }
}
