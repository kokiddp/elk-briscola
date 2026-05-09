using System.Diagnostics.CodeAnalysis;
using Briscola.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Briscola.Infrastructure.Sqlite.Migrations;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct a <see cref="BriscolaDbContext"/>
/// configured for SQLite, without needing to launch the API host. Pointing
/// <c>--project</c> at this assembly is enough.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<BriscolaDbContext>
{
    public BriscolaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BriscolaDbContext>()
            .UseSqlite(
                "Data Source=design-time.db",
                b => b.MigrationsAssembly(typeof(SqliteDesignTimeFactory).Assembly.GetName().Name))
            .Options;
        return new BriscolaDbContext(options);
    }
}
