using Briscola.Infrastructure.Persistence;
using Briscola.Infrastructure.Postgres.Migrations;
using Briscola.Infrastructure.Sqlite.Migrations;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Api.IntegrationTests.Persistence;

/// <summary>
/// Schema-parity check (Step 3.3 spec). Boots both design-time DbContexts
/// in-process (no DB connection required) and compares the EF Core model:
/// every table that exists on one side must exist on the other, and every
/// column likewise. Discrepancies that stay within "expected provider
/// differences" (e.g. column type strings differ between SQLite TEXT and
/// Postgres jsonb for the StateSnapshotJson) are explicitly allowed.
/// </summary>
public sealed class SchemaParityTests
{
    [Fact]
    public void Sqlite_and_Postgres_models_have_the_same_tables()
    {
        var (sqliteTables, postgresTables) = LoadTableNames();

        sqliteTables.Should().BeEquivalentTo(
            postgresTables,
            "the two providers must materialize the same set of tables");
    }

    [Fact]
    public void Sqlite_and_Postgres_models_have_the_same_columns_per_table()
    {
        using BriscolaDbContext sqlite = new SqliteDesignTimeFactory().CreateDbContext([]);
        using BriscolaDbContext postgres = new PostgresDesignTimeFactory().CreateDbContext([]);

        var sqliteShape = ShapeOf(sqlite);
        var postgresShape = ShapeOf(postgres);

        // For every (table, column) on either side, the other side must have
        // it too. We compare the property list, not the SQL column type, so
        // intentional per-provider differences (TEXT vs jsonb) don't fail.
        sqliteShape.Should().BeEquivalentTo(postgresShape);
    }

    [Fact]
    public void Sqlite_and_Postgres_models_have_the_same_indexes_per_table()
    {
        using BriscolaDbContext sqlite = new SqliteDesignTimeFactory().CreateDbContext([]);
        using BriscolaDbContext postgres = new PostgresDesignTimeFactory().CreateDbContext([]);

        var sqliteIndexes = IndexShapeOf(sqlite);
        var postgresIndexes = IndexShapeOf(postgres);

        sqliteIndexes.Should().BeEquivalentTo(postgresIndexes);
    }

    private static (HashSet<string> sqlite, HashSet<string> postgres) LoadTableNames()
    {
        using BriscolaDbContext sqlite = new SqliteDesignTimeFactory().CreateDbContext([]);
        using BriscolaDbContext postgres = new PostgresDesignTimeFactory().CreateDbContext([]);

        var sqliteTables = sqlite.Model.GetEntityTypes()
            .Select(t => t.GetTableName() ?? t.Name)
            .ToHashSet();
        var postgresTables = postgres.Model.GetEntityTypes()
            .Select(t => t.GetTableName() ?? t.Name)
            .ToHashSet();

        return (sqliteTables, postgresTables);
    }

    private static Dictionary<string, HashSet<string>> ShapeOf(BriscolaDbContext context)
    {
        return context.Model.GetEntityTypes().ToDictionary(
            t => t.GetTableName() ?? t.Name,
            t => t.GetProperties().Select(p => p.GetColumnName() ?? p.Name).ToHashSet());
    }

    private static Dictionary<string, HashSet<string>> IndexShapeOf(BriscolaDbContext context)
    {
        return context.Model.GetEntityTypes().ToDictionary(
            t => t.GetTableName() ?? t.Name,
            t => t.GetIndexes()
                .Select(i => $"{(i.IsUnique ? "UQ" : "IX")}:{string.Join(",", i.Properties.Select(p => p.Name))}")
                .ToHashSet());
    }
}
