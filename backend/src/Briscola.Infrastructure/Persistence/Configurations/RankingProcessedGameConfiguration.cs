using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class RankingProcessedGameConfiguration : IEntityTypeConfiguration<RankingProcessedGameEntity>
{
    public void Configure(EntityTypeBuilder<RankingProcessedGameEntity> builder)
    {
        builder.ToTable("RankingProcessedGames");
        builder.HasKey(r => r.GameId);
    }
}
