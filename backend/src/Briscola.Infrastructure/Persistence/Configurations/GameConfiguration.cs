using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class GameConfiguration : IEntityTypeConfiguration<GameEntity>
{
    public void Configure(EntityTypeBuilder<GameEntity> builder)
    {
        builder.ToTable("Games");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(80).IsRequired();
        builder.Property(g => g.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(g => g.BriscolaSuit).HasConversion<string>().HasMaxLength(16).IsRequired();

        // StateSnapshotJson: large text. Per-provider column type configured
        // in the Sqlite/Postgres-specific extension methods at startup.
        builder.Property(g => g.StateSnapshotJson).IsRequired();

        builder.Property(g => g.PasswordHash).HasMaxLength(128);

        // Plain long, application-managed concurrency token. Explicitly NOT
        // IsRowVersion() — the application contract talks in `long Version`.
        builder.Property(g => g.Version).IsRequired();

        builder.HasIndex(g => g.Status);
        builder.HasIndex(g => g.CreatedAt);

        builder.HasMany(g => g.Seats)
            .WithOne()
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
