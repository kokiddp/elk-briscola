using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class GameResultConfiguration : IEntityTypeConfiguration<GameResultEntity>
{
    public void Configure(EntityTypeBuilder<GameResultEntity> builder)
    {
        builder.ToTable("GameResults");
        builder.HasKey(r => r.GameId);

        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.SeatScoresJson).HasMaxLength(128).IsRequired();
        builder.Property(r => r.TeamScoresJson).HasMaxLength(64);
    }
}
