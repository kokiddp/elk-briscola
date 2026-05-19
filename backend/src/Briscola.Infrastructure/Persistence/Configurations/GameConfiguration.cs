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

        // Application-managed concurrency token. NOT IsRowVersion() — we keep
        // a plain long because the contract surfaces `long Version` to the
        // application layer; an SQL-server-style rowversion would force a
        // `byte[]` shape. IsConcurrencyToken() instructs EF to emit
        // `WHERE Version = @old` on every UPDATE so two concurrent writers
        // can't both read v=N and both commit v=N+1 — the loser raises
        // DbUpdateConcurrencyException, which EfGameRepository converts to
        // a `false` return so the application-level retry kicks in.
        builder.Property(g => g.Version).IsRequired().IsConcurrencyToken();

        builder.HasIndex(g => g.Status);
        builder.HasIndex(g => g.CreatedAt);

        builder.HasMany(g => g.Seats)
            .WithOne()
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
