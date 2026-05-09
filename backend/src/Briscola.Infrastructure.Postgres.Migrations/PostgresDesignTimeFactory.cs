using System.Diagnostics.CodeAnalysis;
using Briscola.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Briscola.Infrastructure.Postgres.Migrations;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct a <see cref="BriscolaDbContext"/>
/// configured for PostgreSQL, without needing to launch the API host or
/// connect to a real database (the connection string is design-time only).
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<BriscolaDbContext>
{
    public BriscolaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BriscolaDbContext>()
            .UseNpgsql(
                "Host=design-time;Username=design;Password=design;Database=design",
                b => b.MigrationsAssembly(typeof(PostgresDesignTimeFactory).Assembly.GetName().Name))
            .Options;
        return new BriscolaDbContext(options);
    }
}
