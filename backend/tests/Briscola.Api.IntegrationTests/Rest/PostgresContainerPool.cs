using Npgsql;
using Testcontainers.PostgreSql;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Lazy, process-wide Postgres container shared across every test fixture
/// in this assembly. Each <see cref="BriscolaApiFactory"/> calls
/// <see cref="CreateDatabaseAsync"/> to get its own freshly-created
/// database (per-fixture isolation, ~50 ms each) without paying the
/// container-startup cost more than once.
///
/// We use a manually-managed shared instance rather than xUnit's
/// <c>ICollectionFixture</c> so REST and Hub fixtures (which already
/// have their own collection definitions) can both consume it without
/// being merged into one collection.
///
/// The container starts on first <see cref="StartAsync"/> call and is
/// torn down by the host process exit; xUnit doesn't have an
/// "after-all-tests-in-assembly" hook on AssemblyFixture for this
/// version (xunit 2.9.x), so we accept the leak — Testcontainers'
/// resource-reaper container (Ryuk) will clean it up eventually.
/// </summary>
internal sealed class PostgresContainerPool : IDisposable
{
    public static PostgresContainerPool Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;

    private PostgresContainerPool() { }

    public void Dispose() => _gate.Dispose();

    public async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        await StartAsync(ct).ConfigureAwait(false);

        // Postgres identifiers can't start with a digit and have a
        // 63-char limit; "briscola_test_<32-char-hex>" comfortably fits.
        string dbName = $"briscola_test_{Guid.NewGuid():N}";

        await using NpgsqlConnection admin = new(_adminConnectionString);
        await admin.OpenAsync(ct).ConfigureAwait(false);
        await using (NpgsqlCommand cmd = admin.CreateCommand())
        {
            // No parameter binding for DDL identifiers; the GUID-derived
            // name is hex so it can't be a SQL injection vector.
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Switch the database in the connection string but keep host /
        // port / credentials. Cap Npgsql's per-fixture connection pool
        // (default = 100) — combined with the postgres container's bumped
        // max_connections=400 above, this gives ~25 fixtures × 10 = 250
        // connections of headroom.
        NpgsqlConnectionStringBuilder b = new(_adminConnectionString)
        {
            Database = dbName,
            MaxPoolSize = 10,
            MinPoolSize = 0,
        };
        return b.ConnectionString;
    }

    private async Task StartAsync(CancellationToken ct)
    {
        if (_container is not null)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_container is not null)
            {
                return;
            }

            // Pass `-c max_connections=400` to postgres so parallel test
            // fixtures (each with its own Npgsql pool) don't exhaust the
            // default cap of 100. The postgres docker-entrypoint forwards
            // args starting with "-" to the postgres binary.
            PostgreSqlContainer c = new PostgreSqlBuilder("postgres:17.2-alpine")
                .WithDatabase("postgres")
                .WithUsername("briscola")
                .WithPassword("briscola-test")
                .WithCommand("-c", "max_connections=400")
                .Build();

            await c.StartAsync(ct).ConfigureAwait(false);
            _container = c;
            _adminConnectionString = c.GetConnectionString();
        }
        finally
        {
            _gate.Release();
        }
    }
}
