using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class GameMoveConfiguration : IEntityTypeConfiguration<GameMoveEntity>
{
    public void Configure(EntityTypeBuilder<GameMoveEntity> builder)
    {
        builder.ToTable("GameMoves");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(m => m.PayloadJson).IsRequired();

        // Load-bearing: the move-log hydration path
        // (IGameRepository.GetNextMoveIndexAsync) relies on this uniqueness
        // to detect off-by-one bugs after process restart.
        builder.HasIndex(m => new { m.GameId, m.MoveIndex }).IsUnique();
    }
}
