using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Infrastructure.Persistence;

/// <summary>
/// Combines ASP.NET Identity (users, roles, claims, refresh tokens) with the
/// elk-briscola domain tables. The configurations live in
/// <c>Briscola.Infrastructure.Persistence.Configurations</c> as
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes — kept out of
/// <see cref="OnModelCreating"/> so the context stays thin.
/// </summary>
public sealed class BriscolaDbContext(DbContextOptions<BriscolaDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<GameEntity> Games => Set<GameEntity>();
    public DbSet<GameSeatEntity> GameSeats => Set<GameSeatEntity>();
    public DbSet<GameMoveEntity> GameMoves => Set<GameMoveEntity>();
    public DbSet<GameResultEntity> GameResults => Set<GameResultEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<RankingEntity> Rankings => Set<RankingEntity>();
    public DbSet<RankingProcessedGameEntity> RankingProcessedGames => Set<RankingProcessedGameEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Pull in every IEntityTypeConfiguration<T> in this assembly.
        builder.ApplyConfigurationsFromAssembly(typeof(BriscolaDbContext).Assembly);

        // Provider-specific tweaks (column types, value converters). Detect
        // by provider name so we can serve both Sqlite and Postgres from the
        // same DbContext type without subclassing.
        if (Database.IsSqlite())
        {
            ProviderConfiguration.ApplySqliteConventions(builder);
        }
        else if (Database.IsNpgsql())
        {
            ProviderConfiguration.ApplyPostgresConventions(builder);
        }
    }
}
