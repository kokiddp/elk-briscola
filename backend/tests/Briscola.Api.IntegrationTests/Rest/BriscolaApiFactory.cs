using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Boots the real <c>Briscola.Api</c> host against a per-fixture Postgres
/// database created in a process-wide shared Postgres container. See
/// <see cref="PostgresContainerPool"/> for the container-lifecycle story.
///
/// Each fixture gets its own database name, so individual <c>[Fact]</c>s
/// don't observe each other's users / games. The container itself starts
/// on first use (≈3 s) and is reused for the rest of the test run; per-
/// fixture <c>CREATE DATABASE</c> is sub-100 ms.
/// </summary>
public sealed class BriscolaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string Base64SigningKey { get; } =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Extra in-memory configuration values merged into the host
    /// configuration after the defaults. Tests that need to shorten
    /// timeouts (e.g. <c>Game:ReconnectGraceSeconds</c>) populate this
    /// before <see cref="InitializeAsync"/> runs. Keys here override the
    /// defaults set in <see cref="ConfigureWebHost"/>.
    /// </summary>
    public Dictionary<string, string?> ExtraSettings { get; } = new();

    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresContainerPool.Instance
            .CreateDatabaseAsync()
            .ConfigureAwait(false);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
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
                ["ConnectionStrings:Provider"] = "Postgres",
                ["ConnectionStrings:Default"] = _connectionString
                    ?? throw new InvalidOperationException("InitializeAsync was not awaited."),
                ["Authentication:Jwt:SigningKey"] = Base64SigningKey,
                ["Authentication:Jwt:Issuer"] = "briscola-test",
                ["Authentication:Jwt:Audience"] = "briscola-test",
                ["Migrations:RunOnStartup"] = "true",
                ["Cors:AllowedOrigins:0"] = "http://localhost",
            });
            if (ExtraSettings.Count > 0)
            {
                cfg.AddInMemoryCollection(ExtraSettings);
            }
        });
    }
}
