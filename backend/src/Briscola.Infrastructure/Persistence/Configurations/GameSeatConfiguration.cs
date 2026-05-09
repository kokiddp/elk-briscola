using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class GameSeatConfiguration : IEntityTypeConfiguration<GameSeatEntity>
{
    public void Configure(EntityTypeBuilder<GameSeatEntity> builder)
    {
        builder.ToTable("GameSeats");

        // Composite PK so (GameId, SeatIndex) is unique by construction.
        builder.HasKey(s => new { s.GameId, s.SeatIndex });

        builder.HasIndex(s => s.UserId);
    }
}
