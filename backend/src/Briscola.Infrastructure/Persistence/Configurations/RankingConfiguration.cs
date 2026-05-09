using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class RankingConfiguration : IEntityTypeConfiguration<RankingEntity>
{
    public void Configure(EntityTypeBuilder<RankingEntity> builder)
    {
        builder.ToTable("Rankings");
        builder.HasKey(r => r.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<RankingEntity>(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
