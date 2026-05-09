using Briscola.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Boots the real <c>Briscola.Api</c> host against a per-fixture temporary
/// SQLite file. Each fixture instance gets a unique file under the OS
/// temp directory; <see cref="DisposeAsync"/> deletes it.
///
/// We use a temp <em>file</em> rather than <c>:memory:</c> for two reasons:
/// <list type="bullet">
///   <item>Microsoft.Data.Sqlite's named-shared-cache in-memory mode
///   doesn't isolate databases across <see cref="WebApplicationFactory{T}"/>
///   instances cleanly, even with unique names.</item>
///   <item>Sharing a single open <c>SqliteConnection</c> across every
///   request scope deadlocks SignalR's long-polling traffic
///   (<c>SqliteConnection.CreateFunctionCore</c> races during
///   <c>InitializeDbConnection</c> when multiple in-flight requests open
///   EF scopes concurrently).</item>
/// </list>
/// File-mode SQLite handles concurrent EF scopes via WAL/locks the way
/// real backends do, and per-fixture file isolation gives each fixture
/// a fresh schema. (Testcontainers Postgres remains the Phase 11
/// hardening item.)
/// </summary>
public sealed class BriscolaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string Base64SigningKey { get; } =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public string DatabaseFile { get; } =
        Path.Combine(Path.GetTempPath(), $"briscola-test-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={DatabaseFile}";

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        TryDelete(DatabaseFile);
        TryDelete(DatabaseFile + "-wal");
        TryDelete(DatabaseFile + "-shm");
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
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Authentication:Jwt:SigningKey"] = Base64SigningKey,
                ["Authentication:Jwt:Issuer"] = "briscola-test",
                ["Authentication:Jwt:Audience"] = "briscola-test",
                ["Migrations:RunOnStartup"] = "true",
                ["Cors:AllowedOrigins:0"] = "http://localhost",
            });
        });

        // Replace the DbContext registration so the connection string is
        // taken from this factory directly. The infrastructure module's
        // `AddDbContext` captures `configuration.GetConnectionString(...)`
        // *eagerly* at registration time — before the test's
        // ConfigureAppConfiguration overrides apply — so without this swap
        // the host would fall back to appsettings.json and hit the dev
        // file at `briscola.db` in the test bin folder. (See AGENTS.md
        // section "Configuring authentication options against
        // WebApplicationFactory" for the parallel JWT gotcha.)
        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? dbOptions = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BriscolaDbContext>));
            if (dbOptions is not null)
            {
                services.Remove(dbOptions);
            }

            services.AddDbContext<BriscolaDbContext>(opts =>
                opts.UseSqlite(ConnectionString,
                    b => b.MigrationsAssembly("Briscola.Infrastructure.Sqlite.Migrations")));
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* swallow: another scope may still hold a handle */ }
    }
}
