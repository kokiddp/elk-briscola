using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Briscola.Infrastructure.Persistence;

/// <summary>
/// Provider-specific column type tweaks. Called from <see cref="DependencyInjection"/>
/// and from the per-provider migration startup so the same DbContext model
/// is realised correctly on either backend.
/// </summary>
public static class ProviderConfiguration
{
    /// <summary>
    /// SQLite: store <c>DateTimeOffset</c> as a binary blob (round-trips
    /// timezone offsets exactly, unlike Sqlite's TEXT default), and store
    /// <c>StateSnapshotJson</c> as <c>TEXT</c>.
    /// </summary>
    public static void ApplySqliteConventions(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var dtoConverter = new DateTimeOffsetToBinaryConverter();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dtoConverter);
                }
            }
        }

        modelBuilder.Entity<GameEntity>()
            .Property(g => g.StateSnapshotJson)
            .HasColumnType("TEXT");
    }

    /// <summary>
    /// Postgres: <c>DateTimeOffset</c> maps natively to <c>timestamptz</c>
    /// (no converter needed). <c>StateSnapshotJson</c> uses <c>jsonb</c> for
    /// indexability and on-disk efficiency.
    /// </summary>
    public static void ApplyPostgresConventions(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<GameEntity>()
            .Property(g => g.StateSnapshotJson)
            .HasColumnType("jsonb");
    }
}
